using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Reads Power Automate connector metadata from the environment bound to the
/// profile through the per-environment <c>/powerautomate</c> routes.
/// </summary>
public sealed class ConnectorMetadataService : IConnectorMetadataService
{
    private readonly IConfigurationResolver _resolver;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly PowerAutomateApiClient _client;
    private readonly ILogger<ConnectorMetadataService> _logger;

    public ConnectorMetadataService(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILoggerFactory? loggerFactory = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = loggerFactory?.CreateLogger<ConnectorMetadataService>()
            ?? NullLogger<ConnectorMetadataService>.Instance;
        _client = new PowerAutomateApiClient(tokens, httpFactory, _logger);
    }

    public async Task<IReadOnlyList<ConnectorSummary>> ListConnectorsAsync(
        string? profileName, string? search, CancellationToken ct)
    {
        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        using var document = await _client.SendAsync(
            HttpMethod.Get, PowerAutomateEndpointProvider.Connectors(target.BaseUri), target.Token, null, ct)
            .ConfigureAwait(false);

        var connectors = new List<ConnectorSummary>();
        foreach (var item in JsonRead.Items(document.RootElement))
        {
            var name = JsonRead.String(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = JsonRead.Object(item, "properties") ?? default;
            var connector = new ConnectorSummary(
                Name: name,
                DisplayName: JsonRead.String(properties, "displayName"),
                Description: JsonRead.String(properties, "description"),
                Tier: JsonRead.String(properties, "tier"));

            if (string.IsNullOrWhiteSpace(search)
                || connector.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (connector.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                connectors.Add(connector);
            }
        }

        connectors.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return connectors;
    }

    public async Task<ConnectorDetail> GetConnectorAsync(
        string? profileName, string connector, string? search, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);

        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        using var document = await SendForLookupAsync(
            PowerAutomateEndpointProvider.Connector(target.BaseUri, connector), target.Token,
            $"Connector '{connector}' does not exist in this environment. Find the connector name with 'environment connector list --search <text>'.",
            ct).ConfigureAwait(false);

        return SwaggerOperationIndexer.Index(document.RootElement, connector, search);
    }

    public async Task<IReadOnlyList<OperationSearchHit>> SearchOperationsAsync(
        string? profileName, string search, string? connector, string kind, int top, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(search);

        var body = new JsonObject { ["searchText"] = search };
        if (!string.IsNullOrWhiteSpace(connector))
            body["operationGroupName"] = connector;

        var exclude = new JsonArray("Deprecated");
        switch (kind.ToLowerInvariant())
        {
            case "actions":
                body["allTagsToInclude"] = new JsonArray("Action");
                exclude.Add("Trigger");
                break;
            case "triggers":
                body["allTagsToInclude"] = new JsonArray("Trigger");
                break;
            case "all":
                break;
            default:
                throw new ArgumentException(
                    $"Unknown operation kind '{kind}'. Use 'all', 'actions', or 'triggers'.", nameof(kind));
        }

        body["anyTagsToExclude"] = exclude;

        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        using var document = await _client.SendAsync(
            HttpMethod.Post, PowerAutomateEndpointProvider.OperationSearch(target.BaseUri), target.Token, body, ct)
            .ConfigureAwait(false);

        return ReadSearchHits(document.RootElement, top);
    }

    public async Task<OperationDetail> GetOperationAsync(
        string? profileName, string connector, string operation, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        using var document = await SendForLookupAsync(
            PowerAutomateEndpointProvider.Operation(target.BaseUri, connector, operation), target.Token,
            $"Operation '{operation}' does not exist on connector '{connector}'. List its operations with 'environment connector get {connector}'.",
            ct).ConfigureAwait(false);

        return OperationSchemaReader.Read(connector, operation, document.RootElement);
    }

    /// <summary>
    /// A GET of a named connector or operation, where a 404 means the caller
    /// passed a name that does not exist; the message says how to find the right one.
    /// </summary>
    private async Task<JsonDocument> SendForLookupAsync(Uri uri, string token, string notFoundMessage, CancellationToken ct)
    {
        try
        {
            return await _client.SendAsync(HttpMethod.Get, uri, token, null, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            // Not chained: commands report the innermost message, and the
            // raw 404 body is less useful to the caller than where to look.
            _logger.LogDebug(ex, "Metadata lookup returned not found.");
            throw new ArgumentException(notFoundMessage);
        }
    }

    /// <summary>
    /// The search tag filter does not catch every deprecated operation: some
    /// are only marked in their summary, e.g. <c>mcp_EmailsManagement (deprecated)</c>.
    /// </summary>
    internal static IReadOnlyList<OperationSearchHit> ReadSearchHits(JsonElement root, int top)
    {
        var hits = new List<OperationSearchHit>();
        foreach (var item in JsonRead.Items(root))
        {
            var name = JsonRead.String(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = JsonRead.Object(item, "properties") ?? default;
            var summary = JsonRead.String(properties, "summary");
            if (IsDeprecatedHit(properties, summary))
                continue;

            var group = JsonRead.Object(properties, "operationGroup") ?? default;
            hits.Add(new OperationSearchHit(
                Name: name,
                DisplayName: summary,
                Description: JsonRead.String(properties, "description"),
                ConnectorName: JsonRead.String(group, "name"),
                ConnectorDisplayName: JsonRead.String(group, "displayName")));

            if (top > 0 && hits.Count >= top)
                break;
        }

        return hits;
    }

    private static bool IsDeprecatedHit(JsonElement properties, string? summary)
        => (properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("isDeprecated", out var flag)
                && flag.ValueKind == JsonValueKind.True)
            || string.Equals(JsonRead.String(properties, "status"), "Deprecated", StringComparison.OrdinalIgnoreCase)
            || (summary?.Contains("(deprecated)", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Resolves the profile to its per-environment API host and a token, once
    /// per command, so a command logs and authenticates a single time.
    /// </summary>
    private async Task<EnvironmentTarget> ResolveTargetAsync(string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var environmentId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);
        var baseUri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(
            environmentId, ctx.Connection.Cloud ?? CloudInstance.Public);
        var token = await _client.AcquireTokenAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);

        return new EnvironmentTarget(baseUri, token);
    }

    private async Task<Guid> ResolveEnvironmentIdAsync(ResolvedProfileContext ctx, CancellationToken ct)
    {
        if (ctx.Connection.EnvironmentId.HasValue)
            return ctx.Connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(ctx.Connection.EnvironmentUrl)
            || !Uri.TryCreate(ctx.Connection.EnvironmentUrl, UriKind.Absolute, out var environmentUrl))
        {
            throw new InvalidOperationException(
                $"Connection '{ctx.Connection.Id}' has no EnvironmentUrl or EnvironmentId.");
        }

        _logger.LogDebug(
            "EnvironmentId not stored on connection '{ConnectionId}'. Resolving via Power Platform catalog...",
            ctx.Connection.Id);

        var environment = await _catalog
            .TryGetByEnvironmentUrlAsync(ctx.Connection, ctx.Credential, environmentUrl, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not resolve Power Platform environment for URL '{environmentUrl}'.");

        return environment.EnvironmentId;
    }

    private sealed record EnvironmentTarget(Uri BaseUri, string Token);
}
