using System.Net.Http.Headers;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>A connector connection from the Power Platform API. <see cref="Id"/> is its name (the connectionid).</summary>
public sealed record PowerPlatformConnection(
    string Id,
    string? ConnectorId,
    string? DisplayName,
    string? Status);

public interface IPowerPlatformConnectionCatalog
{
    /// <summary>Lists the connector connections in the given environment.</summary>
    Task<IReadOnlyList<PowerPlatformConnection>> ListAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        CancellationToken ct);
}

/// <summary>Lists connector connections in an environment via the Power Platform connections API (admin scope).</summary>
public sealed class PowerPlatformConnectionCatalog : IPowerPlatformConnectionCatalog
{
    private const string ApiVersion = "2016-11-01";
    private static readonly Uri PowerAppsAudience = new("https://service.powerapps.com/");

    private readonly IAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public PowerPlatformConnectionCatalog(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public async Task<IReadOnlyList<PowerPlatformConnection>> ListAsync(
        Connection connection,
        Credential credential,
        Guid environmentId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var baseUri = GetApiBaseUri(connection.Cloud ?? CloudInstance.Public);
        var token = await _tokens.AcquireForResourceAsync(connection, credential, PowerAppsAudience, ct).ConfigureAwait(false);

        using var http = _httpFactory.Create();
        var connections = new List<PowerPlatformConnection>();
        Uri? nextPage = new(baseUri,
            $"/providers/Microsoft.PowerApps/scopes/admin/environments/{environmentId}/connections?api-version={ApiVersion}");

        while (nextPage is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextPage);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Power Platform connection lookup failed ({(int)response.StatusCode} {response.ReasonPhrase}) against '{nextPage}': {Truncate(body, 500)}");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Power Platform connection lookup returned a payload without a 'value' array.");

            foreach (var item in items.EnumerateArray())
            {
                if (TryParseConnection(item, out var parsed))
                    connections.Add(parsed);
            }

            nextPage = TryReadNextLink(root, baseUri);
        }

        return connections;
    }

    private static bool TryParseConnection(JsonElement item, out PowerPlatformConnection connection)
    {
        connection = null!;

        if (!item.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
            return false;

        var id = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(id))
            return false;

        string? connectorId = null;
        string? displayName = null;
        string? status = null;
        if (item.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            connectorId = TryReadString(properties, "apiId");
            displayName = TryReadString(properties, "displayName");
            if (properties.TryGetProperty("statuses", out var statuses)
                && statuses.ValueKind == JsonValueKind.Array
                && statuses.GetArrayLength() > 0
                && statuses[0].ValueKind == JsonValueKind.Object)
            {
                status = TryReadString(statuses[0], "status");
            }
        }

        connection = new PowerPlatformConnection(id.Trim(), connectorId, displayName, status);
        return true;
    }

    private static Uri? TryReadNextLink(JsonElement root, Uri baseUri)
    {
        var nextLink = TryReadString(root, "nextLink");
        if (string.IsNullOrWhiteSpace(nextLink))
            return null;

        if (Uri.TryCreate(nextLink, UriKind.Absolute, out var absolute))
            return absolute;

        return Uri.TryCreate(baseUri, nextLink, out var relative) ? relative : null;
    }

    private static string? TryReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()?.Trim()
            : null;

    private static Uri GetApiBaseUri(CloudInstance cloud)
        => cloud switch
        {
            CloudInstance.Public or CloudInstance.Gcc => new Uri("https://api.powerapps.com/"),
            _ => throw new NotSupportedException(
                $"Power Platform connection lookup is not wired for cloud '{cloud}' in this release."),
        };

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
