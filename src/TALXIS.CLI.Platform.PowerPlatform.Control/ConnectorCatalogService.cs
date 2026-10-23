using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerApps;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Discovers connectors and their operations in the profile's environment via
/// the Power Apps connector API. Reuses the BAP transport because both APIs
/// validate the same <c>service.powerapps.com</c> audience, so one cached
/// token serves both.
/// </summary>
public sealed class ConnectorCatalogService : IConnectorCatalogService
{
    private readonly IConfigurationResolver _resolver;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly BapAdminApiClient _client;
    private readonly IHttpClientFactoryWrapper _httpFactory;
    private readonly ILogger<ConnectorCatalogService> _logger;

    public ConnectorCatalogService(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILoggerFactory? loggerFactory = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _client = new BapAdminApiClient(tokens, httpFactory);
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
        _logger = loggerFactory?.CreateLogger<ConnectorCatalogService>()
            ?? NullLogger<ConnectorCatalogService>.Instance;
    }

    public async Task<IReadOnlyList<ConnectorSummary>> ListConnectorsAsync(
        string? profileName, CancellationToken ct)
    {
        var (ctx, baseUri, environmentId) = await PrepareAsync(profileName, ct).ConfigureAwait(false);
        var token = await _client.AcquireTokenAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);

        var connectors = new List<ConnectorSummary>();
        Uri? nextPage = new(baseUri,
            $"providers/Microsoft.PowerApps/apis?api-version={PowerAppsEndpointProvider.ApiVersion}&$filter={BuildEnvironmentFilter(environmentId)}");

        while (nextPage is not null)
        {
            var response = await _client.SendAsync(HttpMethod.Get, nextPage, token, jsonBody: null, ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Power Apps connector lookup failed ({(int)response.StatusCode} {response.StatusCode}) against '{nextPage}': {BapAdminApiClient.Truncate(response.Body, 500)}");
            }

            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Power Apps connector lookup returned a payload without a 'value' array.");

            foreach (var item in items.EnumerateArray())
            {
                if (TryParseConnector(item, out var connector))
                    connectors.Add(connector);
            }

            nextPage = TryReadNextLink(root, baseUri);
        }

        return connectors
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ConnectorOperationSummary>> ListOperationsAsync(
        string? profileName, string connectorName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorName);

        var swagger = await GetSwaggerAsync(profileName, connectorName, ct).ConfigureAwait(false);
        return ConnectorSwaggerParser.ListOperations(swagger);
    }

    public async Task<ConnectorOperationDetail?> GetOperationAsync(
        string? profileName, string connectorName, string operationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        var swagger = await GetSwaggerAsync(profileName, connectorName, ct).ConfigureAwait(false);
        return ConnectorSwaggerParser.GetOperation(swagger, connectorName, BuildApiId(connectorName), operationId);
    }

    private async Task<JsonElement> GetSwaggerAsync(string? profileName, string connectorName, CancellationToken ct)
    {
        var (ctx, baseUri, environmentId) = await PrepareAsync(profileName, ct).ConfigureAwait(false);
        var token = await _client.AcquireTokenAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);

        var requestUri = new Uri(baseUri,
            $"providers/Microsoft.PowerApps/apis/{Uri.EscapeDataString(connectorName)}?api-version={PowerAppsEndpointProvider.ApiVersion}&$expand=properties.swagger&$filter={BuildEnvironmentFilter(environmentId)}");

        var response = await _client.SendAsync(HttpMethod.Get, requestUri, token, jsonBody: null, ct).ConfigureAwait(false);
        // The API answers 404 for a deleted environment too, so the body tells the two apart.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Connector '{connectorName}' or the environment was not found. Verify the connector name with 'txc environment connector list'. Response: {BapAdminApiClient.Truncate(response.Body, 300)}");
        }

        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Power Apps connector lookup for '{connectorName}' failed ({(int)response.StatusCode} {response.StatusCode}) against '{requestUri}': {BapAdminApiClient.Truncate(response.Body, 500)}");
        }

        using (var document = JsonDocument.Parse(response.Body))
        {
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object)
            {
                if (properties.TryGetProperty("swagger", out var swagger) && swagger.ValueKind == JsonValueKind.Object)
                    return swagger.Clone();

                // Some connectors omit the inline swagger even with $expand; their
                // definition is only reachable through the apiDefinitions blob URL.
                if (properties.TryGetProperty("apiDefinitions", out var definitions)
                    && definitions.ValueKind == JsonValueKind.Object
                    && definitions.TryGetProperty("originalSwaggerUrl", out var swaggerUrl)
                    && swaggerUrl.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(swaggerUrl.GetString(), UriKind.Absolute, out var blobUri))
                {
                    _logger.LogInformation(
                        "Connector {Connector} has no inline swagger. Downloading definition from apiDefinitions.",
                        connectorName);
                    return await DownloadSwaggerAsync(blobUri, ct).ConfigureAwait(false);
                }
            }
        }

        throw new InvalidOperationException(
            $"Connector '{connectorName}' returned no OpenAPI definition. Verify the connector name with 'txc environment connector list'.");
    }

    private async Task<JsonElement> DownloadSwaggerAsync(Uri blobUri, CancellationToken ct)
    {
        // The apiDefinitions URL is a pre-signed blob link; an Authorization
        // header would make Azure Storage reject the request, so plain GET.
        using var http = _httpFactory.Create();
        using var response = await http.GetAsync(blobUri, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Downloading the connector OpenAPI definition failed ({(int)response.StatusCode} {response.StatusCode}) against '{blobUri}': {BapAdminApiClient.Truncate(body, 500)}");
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"The connector OpenAPI definition downloaded from '{blobUri}' is not a JSON object.");
        }

        return document.RootElement.Clone();
    }

    private async Task<(ResolvedProfileContext Context, Uri BaseUri, Guid EnvironmentId)> PrepareAsync(
        string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var baseUri = PowerAppsEndpointProvider.GetApiBaseUri(ctx.Connection.Cloud ?? CloudInstance.Public);
        var environmentId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);
        return (ctx, baseUri, environmentId);
    }

    private async Task<Guid> ResolveEnvironmentIdAsync(ResolvedProfileContext ctx, CancellationToken ct)
    {
        if (ctx.Connection.EnvironmentId.HasValue)
            return ctx.Connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(ctx.Connection.EnvironmentUrl)
            || !Uri.TryCreate(ctx.Connection.EnvironmentUrl, UriKind.Absolute, out var envUri))
        {
            throw new InvalidOperationException(
                $"Connection '{ctx.Connection.Id}' has no EnvironmentUrl or EnvironmentId.");
        }

        _logger.LogInformation(
            "EnvironmentId not stored on connection '{ConnectionId}'. Resolving via Power Platform catalog...",
            ctx.Connection.Id);

        var env = await _catalog
            .TryGetByEnvironmentUrlAsync(ctx.Connection, ctx.Credential, envUri, ct)
            .ConfigureAwait(false);

        if (env is null)
        {
            throw new InvalidOperationException(
                $"Could not resolve Power Platform environment for URL '{ctx.Connection.EnvironmentUrl}'.");
        }

        return env.EnvironmentId;
    }

    private static string BuildEnvironmentFilter(Guid environmentId)
        => Uri.EscapeDataString($"environment eq '{environmentId:D}'");

    private static string BuildApiId(string connectorName)
        => $"/providers/Microsoft.PowerApps/apis/{connectorName}";

    private static bool TryParseConnector(JsonElement item, out ConnectorSummary connector)
    {
        connector = null!;

        if (!item.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameElement.GetString()))
        {
            return false;
        }

        string? displayName = null;
        string? tier = null;
        string? description = null;
        var isCustomApi = false;

        if (item.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            displayName = TryReadOptionalString(properties, "displayName");
            tier = TryReadOptionalString(properties, "tier");
            description = TryReadOptionalString(properties, "description");
            isCustomApi = properties.TryGetProperty("isCustomApi", out var custom)
                && (custom.ValueKind == JsonValueKind.True
                    || (custom.ValueKind == JsonValueKind.String
                        && bool.TryParse(custom.GetString(), out var parsed) && parsed));
        }

        var name = nameElement.GetString()!.Trim();
        connector = new ConnectorSummary(
            Name: name,
            DisplayName: displayName ?? name,
            Tier: tier,
            IsCustomApi: isCustomApi,
            Description: description);
        return true;
    }

    private static Uri? TryReadNextLink(JsonElement root, Uri baseUri)
    {
        if (!root.TryGetProperty("nextLink", out var nextLinkElement)
            || nextLinkElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nextLinkElement.GetString()))
        {
            return null;
        }

        var nextLink = nextLinkElement.GetString()!;
        if (Uri.TryCreate(nextLink, UriKind.Absolute, out var absolute))
            return absolute;

        return Uri.TryCreate(baseUri, nextLink, out var relative) ? relative : null;
    }

    private static string? TryReadOptionalString(JsonElement element, string property)
        => element.TryGetProperty(property, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()?.Trim()
            : null;
}
