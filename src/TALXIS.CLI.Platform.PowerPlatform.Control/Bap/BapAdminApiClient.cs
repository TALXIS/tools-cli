using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Bap;

internal sealed record BapAdminApplicationRegistration(Guid ApplicationId);

/// <summary>
/// Thin authenticated transport over the BAP admin API. Owns the cross-cutting
/// concerns shared by every BAP caller — token acquisition, base-URI
/// resolution, bearer-authorized JSON requests, and long-running operation
/// polling — so higher-level services (catalog, provisioner) contain only
/// endpoint-specific request building and response parsing.
/// </summary>
internal sealed class BapAdminApiClient
{
    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public BapAdminApiClient(IAccessTokenService tokens, IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    /// <summary>Resolves the BAP admin base URI for the connection's cloud.</summary>
    public Uri GetBaseUri(Connection connection)
        => BapEndpointProvider.GetAdminApiBaseUri(connection.Cloud ?? CloudInstance.Public);

    /// <summary>Acquires a BAP admin bearer token for the (connection, credential) identity.</summary>
    public Task<string> AcquireTokenAsync(Connection connection, Credential credential, CancellationToken ct)
        => _tokens.AcquireForResourceAsync(connection, credential, BapEndpointProvider.PowerAppsAudience, ct);

    /// <summary>
    /// Sends a bearer-authorized request and returns the raw outcome (status,
    /// body, and the <c>Location</c> header used to poll async operations).
    /// The caller owns success/error interpretation so each endpoint can craft
    /// its own diagnostic message.
    /// </summary>
    public async Task<BapResponse> SendAsync(
        HttpMethod method,
        Uri requestUri,
        string token,
        object? jsonBody,
        CancellationToken ct)
    {
        using var http = _httpFactory.Create();
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonBody is not null)
        {
            var json = JsonSerializer.Serialize(jsonBody, BapJsonOptions.Default);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new BapResponse(response.StatusCode, body, response.Headers.Location);
    }

    public async Task<IReadOnlyList<BapAdminApplicationRegistration>> ListAdminApplicationsAsync(
        Connection connection,
        Credential credential,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var token = await AcquireTokenAsync(connection, credential, ct).ConfigureAwait(false);
        var initialRequestUri = new Uri(
            GetBaseUri(connection),
            "/providers/Microsoft.BusinessAppPlatform/adminApplications?api-version=2021-04-01");

        return await ODataPagingSupport.FetchAllPagesAsync(
            initialRequestUri,
            async (requestUri, pageCt) =>
            {
                var response = await SendAsync(HttpMethod.Get, requestUri, token, jsonBody: null, pageCt).ConfigureAwait(false);
                if (!response.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"BAP admin application list failed ({(int)response.StatusCode} {response.StatusCode}): {Truncate(response.Body, 500)}");
                }

                return response.Body;
            },
            item => item.TryGetProperty("applicationId", out var applicationIdElement)
                && applicationIdElement.ValueKind == JsonValueKind.String
                && Guid.TryParse(applicationIdElement.GetString(), out var applicationId)
                    ? new BapAdminApplicationRegistration(applicationId)
                    : null,
            "BAP admin application list payload did not contain a \"value\" array.",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Provisions a Dataverse <c>systemuser</c> record for the given Entra
    /// object ID in the target environment, without requiring the user to
    /// have ever signed in (background JIT sync would otherwise be the only
    /// way this record gets created). Safe to call again for a user who
    /// already has access.
    /// </summary>
    public async Task AddUserToEnvironmentAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        Guid userAadObjectId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var token = await AcquireTokenAsync(connection, credential, ct).ConfigureAwait(false);
        var requestUri = new Uri(
            GetBaseUri(connection),
            $"/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments/{environmentId}/addUser?api-version=2021-04-01");

        var body = new Dictionary<string, object> { ["ObjectId"] = userAadObjectId };
        var response = await SendAsync(HttpMethod.Post, requestUri, token, body, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to add user '{userAadObjectId}' to environment '{environmentId}' ({(int)response.StatusCode} {response.StatusCode}): {Truncate(response.Body, 500)}");
        }
    }

    public async Task RegisterAdminApplicationAsync(
        Connection connection,
        Credential credential,
        Guid clientId,
        CancellationToken ct)
    {
        await SendAdminApplicationMutationAsync(connection, credential, clientId, HttpMethod.Put, ct)
            .ConfigureAwait(false);
    }

    public async Task UnregisterAdminApplicationAsync(
        Connection connection,
        Credential credential,
        Guid clientId,
        CancellationToken ct)
    {
        await SendAdminApplicationMutationAsync(connection, credential, clientId, HttpMethod.Delete, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Truncates a (potentially large) response body for inclusion in error
    /// messages without dumping a full payload to the log.
    /// </summary>
    public static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");

    private async Task SendAdminApplicationMutationAsync(
        Connection connection,
        Credential credential,
        Guid clientId,
        HttpMethod method,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var token = await AcquireTokenAsync(connection, credential, ct).ConfigureAwait(false);
        var requestUri = new Uri(
            GetBaseUri(connection),
            $"/providers/Microsoft.BusinessAppPlatform/adminApplications/{clientId}?api-version=2021-04-01");

        var response = await SendAsync(method, requestUri, token, jsonBody: null, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            var action = method == HttpMethod.Put ? "register" : "unregister";
            throw new InvalidOperationException(
                $"BAP admin application {action} failed ({(int)response.StatusCode} {response.StatusCode}): {Truncate(response.Body, 500)}");
        }
    }
}

/// <summary>Raw result of a BAP admin API call.</summary>
internal readonly record struct BapResponse(HttpStatusCode StatusCode, string Body, Uri? Location)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
}
