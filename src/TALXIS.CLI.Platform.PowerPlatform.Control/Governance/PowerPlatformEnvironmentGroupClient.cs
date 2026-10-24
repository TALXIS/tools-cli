using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Governance;

/// <summary>
/// Thin authenticated client over the environment-group management endpoints
/// under <c>api.powerplatform.com/environmentmanagement/environmentGroups</c>.
/// Membership mutations (<see cref="IPowerPlatformEnvironmentGroupClient.AddEnvironmentAsync"/>/
/// <see cref="IPowerPlatformEnvironmentGroupClient.RemoveEnvironmentAsync"/>)
/// are asynchronous on the service side (<c>202 Accepted</c> + an operation
/// to poll); this client polls the operation to completion before returning.
/// </summary>
public sealed class PowerPlatformEnvironmentGroupClient : IPowerPlatformEnvironmentGroupClient
{
    private const string ApiVersion = "2024-10-01";
    private static readonly Uri PowerPlatformApiAudience = new("https://api.powerplatform.com/");
    private static readonly TimeSpan OperationPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OperationPollTimeout = TimeSpan.FromMinutes(5);

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public PowerPlatformEnvironmentGroupClient(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public async Task<IReadOnlyList<PowerPlatformEnvironmentGroup>> ListAsync(
        Connection connection, Credential credential, CancellationToken ct)
    {
        var initialRequestUri = BuildUri(connection, $"environmentmanagement/environmentGroups?api-version={ApiVersion}");

        return await ODataPagingSupport.FetchAllPagesAsync(
            initialRequestUri,
            (requestUri, pageCt) => SendForBodyAsync(connection, credential, HttpMethod.Get, requestUri, jsonBody: null, pageCt),
            item => TryParseEnvironmentGroup(item, out var group) ? group : null,
            "Environment group payload did not contain a 'value' array.",
            ct).ConfigureAwait(false);
    }

    public async Task<PowerPlatformEnvironmentGroup?> GetAsync(
        Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct)
    {
        var body = await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Get,
            BuildUri(connection, $"environmentmanagement/environmentGroups/{environmentGroupId}?api-version={ApiVersion}"),
            jsonBody: null,
            ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(body))
            return null;

        using var document = JsonDocument.Parse(body);
        return TryParseEnvironmentGroup(document.RootElement, out var group) ? group : null;
    }

    public async Task<PowerPlatformEnvironmentGroup> CreateAsync(
        Connection connection, Credential credential, PowerPlatformEnvironmentGroupCreateOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DisplayName);

        var requestBody = new
        {
            displayName = options.DisplayName,
            description = options.Description,
        };

        var responseBody = await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Post,
            BuildUri(connection, $"environmentmanagement/environmentGroups?api-version={ApiVersion}"),
            requestBody,
            ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(responseBody);
        return TryParseEnvironmentGroup(document.RootElement, out var group)
            ? group
            : throw new InvalidOperationException("Environment group creation response could not be parsed.");
    }

    public async Task<PowerPlatformEnvironmentGroup> UpdateAsync(
        Connection connection, Credential credential, Guid environmentGroupId, PowerPlatformEnvironmentGroupUpdateOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var requestBody = new Dictionary<string, object?>();
        if (options.DisplayName is not null)
            requestBody["displayName"] = options.DisplayName;
        if (options.Description is not null)
            requestBody["description"] = options.Description;

        var responseBody = await SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Patch,
            BuildUri(connection, $"environmentmanagement/environmentGroups/{environmentGroupId}?api-version={ApiVersion}"),
            requestBody,
            ct).ConfigureAwait(false);

        using var document = JsonDocument.Parse(responseBody);
        return TryParseEnvironmentGroup(document.RootElement, out var group)
            ? group
            : throw new InvalidOperationException("Environment group update response could not be parsed.");
    }

    public Task DeleteAsync(Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct)
        => SendForBodyAsync(
            connection,
            credential,
            HttpMethod.Delete,
            BuildUri(connection, $"environmentmanagement/environmentGroups/{environmentGroupId}?api-version={ApiVersion}"),
            jsonBody: null,
            ct);

    public async Task AddEnvironmentAsync(
        Connection connection, Credential credential, Guid environmentGroupId, Guid environmentId, CancellationToken ct)
    {
        await SendAndAwaitOperationAsync(
            connection,
            credential,
            HttpMethod.Post,
            BuildUri(connection, $"environmentmanagement/environmentGroups/{environmentGroupId}/addEnvironment/{environmentId}?api-version={ApiVersion}"),
            ct).ConfigureAwait(false);
    }

    public async Task RemoveEnvironmentAsync(
        Connection connection, Credential credential, Guid environmentGroupId, Guid environmentId, CancellationToken ct)
    {
        await SendAndAwaitOperationAsync(
            connection,
            credential,
            HttpMethod.Post,
            BuildUri(connection, $"environmentmanagement/environmentGroups/{environmentGroupId}/removeEnvironment/{environmentId}?api-version={ApiVersion}"),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an async (202-returning) request and polls the returned
    /// operation-status URL (<c>Location</c>/<c>Operation-Location</c>
    /// header, per the standard Azure async-operation pattern) until it
    /// reports completion or <see cref="OperationPollTimeout"/> elapses.
    /// </summary>
    private async Task SendAndAwaitOperationAsync(
        Connection connection, Credential credential, HttpMethod method, Uri requestUri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var token = await _tokens.AcquireForResourceAsync(connection, credential, PowerPlatformApiAudience, ct)
            .ConfigureAwait(false);

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Environment group membership request failed ({(int)response.StatusCode} {response.ReasonPhrase}) against '{requestUri}': {PowerPlatformRbacClient.Truncate(body, 500)}");
            }

            return;
        }

        var operationUri = response.Headers.Location
            ?? (response.Headers.TryGetValues("Operation-Location", out var values) ? new Uri(values.First()) : null);

        if (operationUri is null)
            return;

        var deadline = DateTimeOffset.UtcNow + OperationPollTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(OperationPollInterval, ct).ConfigureAwait(false);

            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, operationUri);
            pollRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var pollResponse = await http.SendAsync(pollRequest, ct).ConfigureAwait(false);
            var pollBody = await pollResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (pollResponse.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                if (!pollResponse.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Environment group membership operation failed ({(int)pollResponse.StatusCode} {pollResponse.ReasonPhrase}): {PowerPlatformRbacClient.Truncate(pollBody, 500)}");
                }

                return;
            }
        }

        throw new TimeoutException(
            $"Environment group membership operation did not complete within {OperationPollTimeout}.");
    }

    private async Task<string> SendForBodyAsync(
        Connection connection, Credential credential, HttpMethod method, Uri requestUri, object? jsonBody, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var token = await _tokens.AcquireForResourceAsync(connection, credential, PowerPlatformApiAudience, ct)
            .ConfigureAwait(false);

        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonBody is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(jsonBody), Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Environment group request failed ({(int)response.StatusCode} {response.ReasonPhrase}) against '{requestUri}': {PowerPlatformRbacClient.Truncate(responseBody, 500)}");
        }

        return responseBody;
    }

    private static Uri BuildUri(Connection connection, string relativePath)
        => new(GetBaseUri(connection.Cloud ?? CloudInstance.Public), relativePath);

    private static Uri GetBaseUri(CloudInstance cloud) => cloud switch
    {
        CloudInstance.Public or CloudInstance.Gcc => new Uri("https://api.powerplatform.com/"),
        _ => throw new NotSupportedException(
            $"Environment groups are not wired for cloud '{cloud}' in this release."),
    };

    private static bool TryParseEnvironmentGroup(JsonElement item, out PowerPlatformEnvironmentGroup group)
    {
        group = null!;

        if (!TryReadGuid(item, "id", out var id) || !TryReadString(item, "displayName", out var displayName))
            return false;

        var environmentIds = new List<Guid>();
        if (item.TryGetProperty("environments", out var environments) && environments.ValueKind == JsonValueKind.Array)
        {
            foreach (var env in environments.EnumerateArray())
            {
                if (env.ValueKind == JsonValueKind.Object
                    && env.TryGetProperty("id", out var envId)
                    && envId.ValueKind == JsonValueKind.String
                    && Guid.TryParse(envId.GetString(), out var parsedEnvId))
                {
                    environmentIds.Add(parsedEnvId);
                }
                else if (env.ValueKind == JsonValueKind.String && Guid.TryParse(env.GetString(), out var directEnvId))
                {
                    environmentIds.Add(directEnvId);
                }
            }
        }

        group = new PowerPlatformEnvironmentGroup(
            id,
            displayName,
            TryReadOptionalString(item, "description"),
            TryReadOptionalDateTimeOffset(item, "createdOn"),
            TryReadOptionalGuid(item, "createdByPrincipalObjectId"),
            TryReadOptionalDateTimeOffset(item, "lastModifiedOn"),
            environmentIds);
        return true;
    }

    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        return Guid.TryParse(property.GetString(), out value);
    }

    private static Guid? TryReadOptionalGuid(JsonElement element, string propertyName)
        => TryReadGuid(element, propertyName, out var value) ? value : null;

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw.Trim();
        return true;
    }

    private static string? TryReadOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;

    private static DateTimeOffset? TryReadOptionalDateTimeOffset(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
           && property.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(property.GetString(), out var value)
            ? value
            : null;
}
