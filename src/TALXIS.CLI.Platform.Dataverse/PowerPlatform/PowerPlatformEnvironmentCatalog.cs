using System.Net.Http.Headers;
using System.Text.Json;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.PowerPlatform;

/// <summary>
/// Metadata about a Power Platform environment returned by the admin API.
/// Kept small so bootstrap flows and future environment-listing commands can
/// share one model without dragging along the full raw payload.
/// </summary>
public sealed record PowerPlatformEnvironmentSummary(
    Guid EnvironmentId,
    string DisplayName,
    Uri EnvironmentUrl,
    string? UniqueName,
    string? DomainName,
    Guid? OrganizationId);

public interface IPowerPlatformEnvironmentCatalog
{
    Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(
        TALXIS.CLI.Core.Model.Connection connection,
        Credential credential,
        CancellationToken ct);

    Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(
        TALXIS.CLI.Core.Model.Connection connection,
        Credential credential,
        Uri environmentUrl,
        CancellationToken ct);
}

/// <summary>
/// Queries the Power Platform admin API for Dataverse-backed environments.
/// This is intentionally reusable so future commands can list environments
/// without duplicating auth, paging, or response parsing.
/// </summary>
public sealed class PowerPlatformEnvironmentCatalog : IPowerPlatformEnvironmentCatalog
{
    private const string ApiVersion = "2020-10-01";

    private readonly IDataverseAccessTokenService _tokens;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public PowerPlatformEnvironmentCatalog(
        IDataverseAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public async Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(
        TALXIS.CLI.Core.Model.Connection connection,
        Credential credential,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);

        var baseUri = GetAdminApiBaseUri(connection.Cloud ?? CloudInstance.Public);
        var token = await _tokens.AcquireForResourceAsync(connection, credential, baseUri, ct).ConfigureAwait(false);

        using var http = _httpFactory.Create();
        var environments = new List<PowerPlatformEnvironmentSummary>();
        Uri? nextPage = new(baseUri, $"/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version={ApiVersion}");

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
                    $"Power Platform environment lookup failed ({(int)response.StatusCode} {response.ReasonPhrase}) against '{nextPage}': {Truncate(body, 500)}");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Power Platform environment lookup returned a payload without a 'value' array.");

            foreach (var item in items.EnumerateArray())
            {
                if (TryParseEnvironment(item, out var environment))
                    environments.Add(environment);
            }

            nextPage = TryReadNextLink(root, baseUri);
        }

        return environments;
    }

    public async Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(
        TALXIS.CLI.Core.Model.Connection connection,
        Credential credential,
        Uri environmentUrl,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(environmentUrl);

        var environments = await ListAsync(connection, credential, ct).ConfigureAwait(false);
        return environments.SingleOrDefault(e => UrlEquals(e.EnvironmentUrl, environmentUrl));
    }

    private static bool TryParseEnvironment(JsonElement item, out PowerPlatformEnvironmentSummary environment)
    {
        environment = null!;

        if (!item.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(nameElement.GetString(), out var environmentId))
            return false;

        if (!item.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object
            || !TryReadString(properties, "displayName", out var displayName)
            || !properties.TryGetProperty("linkedEnvironmentMetadata", out var linked)
            || linked.ValueKind != JsonValueKind.Object
            || !TryReadString(linked, "instanceUrl", out var instanceUrl)
            || !Uri.TryCreate(instanceUrl, UriKind.Absolute, out var environmentUrl))
            return false;

        environment = new PowerPlatformEnvironmentSummary(
            EnvironmentId: environmentId,
            DisplayName: displayName,
            EnvironmentUrl: NormalizeEnvironmentUrl(environmentUrl),
            UniqueName: TryReadOptionalString(linked, "uniqueName"),
            DomainName: TryReadOptionalString(linked, "domainName"),
            OrganizationId: TryReadOptionalGuid(linked, "resourceId"));
        return true;
    }

    private static Uri? TryReadNextLink(JsonElement root, Uri baseUri)
    {
        if (!TryReadString(root, "nextLink", out var nextLink))
            return null;

        if (Uri.TryCreate(nextLink, UriKind.Absolute, out var absolute))
            return absolute;

        return Uri.TryCreate(baseUri, nextLink, out var relative) ? relative : null;
    }

    private static bool TryReadString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.String)
            return false;

        var raw = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        value = raw.Trim();
        return true;
    }

    private static string? TryReadOptionalString(JsonElement element, string property)
        => element.TryGetProperty(property, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()?.Trim()
            : null;

    private static Guid? TryReadOptionalGuid(JsonElement element, string property)
        => element.TryGetProperty(property, out var propertyElement)
           && propertyElement.ValueKind == JsonValueKind.String
           && Guid.TryParse(propertyElement.GetString(), out var parsed)
            ? parsed
            : null;

    private static bool UrlEquals(Uri left, Uri right)
        => NormalizeEnvironmentUrl(left).AbsoluteUri.Equals(
            NormalizeEnvironmentUrl(right).AbsoluteUri,
            StringComparison.OrdinalIgnoreCase);

    private static Uri NormalizeEnvironmentUrl(Uri uri)
        => new(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");

    private static Uri GetAdminApiBaseUri(CloudInstance cloud)
        => cloud switch
        {
            CloudInstance.Public or CloudInstance.Gcc => new Uri("https://api.bap.microsoft.com/"),
            _ => throw new NotSupportedException(
                $"Power Platform environment lookup is not wired for cloud '{cloud}' in this release. Pass --name explicitly."),
        };

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
