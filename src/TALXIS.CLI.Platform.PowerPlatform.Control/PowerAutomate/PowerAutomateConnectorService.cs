using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

/// <summary>
/// Reads Power Automate connector metadata from the environment bound to the
/// active profile. Read-only by design: flows are authored locally as solution
/// components, and this service exists so those definitions can be checked
/// against connectors that actually exist.
/// </summary>
public sealed class PowerAutomateConnectorService : IPowerAutomateConnectorService
{
    /// <summary>
    /// Connector and operation payloads are stable across an authoring session
    /// and each fetch costs a round trip, so they are cached briefly. The
    /// validator hits the same operations repeatedly while walking a flow.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly IConfigurationResolver _resolver;
    private readonly IPowerPlatformEnvironmentCatalog _catalog;
    private readonly IDataverseQueryService _query;
    private readonly PowerAutomateApiClient _client;
    private readonly ILogger<PowerAutomateConnectorService> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public PowerAutomateConnectorService(
        IConfigurationResolver resolver,
        IPowerPlatformEnvironmentCatalog catalog,
        IDataverseQueryService query,
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null,
        ILoggerFactory? loggerFactory = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _logger = loggerFactory?.CreateLogger<PowerAutomateConnectorService>()
            ?? NullLogger<PowerAutomateConnectorService>.Instance;
        _client = new PowerAutomateApiClient(
            tokens ?? throw new ArgumentNullException(nameof(tokens)),
            httpFactory,
            loggerFactory?.CreateLogger<PowerAutomateApiClient>());
    }

    public async Task<IReadOnlyList<ConnectorSummary>> ListConnectorsAsync(
        string? profileName, string? query, int? top, CancellationToken ct)
    {
        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        var uri = PowerAutomateEndpointProvider.Connectors(target.BaseUri, top);

        using var document = await SendAsync(HttpMethod.Get, uri, target, null, ct).ConfigureAwait(false);

        var connectors = new List<ConnectorSummary>();
        foreach (var item in EnumerateValue(document.RootElement))
        {
            var name = SwaggerOperationIndexer.TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = SwaggerOperationIndexer.TryGetObject(item, "properties");
            connectors.Add(new ConnectorSummary(
                Name: name,
                DisplayName: properties is { } p ? SwaggerOperationIndexer.TryGetString(p, "displayName") : null,
                Description: properties is { } d ? SwaggerOperationIndexer.TryGetString(d, "description") : null,
                Tier: properties is { } t ? SwaggerOperationIndexer.TryGetString(t, "tier") : null));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            connectors = connectors
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (c.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        connectors.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return connectors;
    }

    public async Task<ConnectorDetail> GetConnectorAsync(
        string? profileName, string connector, string? query, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);

        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        var payload = await GetConnectorPayloadAsync(target, connector, ct).ConfigureAwait(false);
        return SwaggerOperationIndexer.Index(payload, connector, query);
    }

    public async Task<IReadOnlyList<OperationSearchHit>> SearchOperationsAsync(
        string? profileName, string? query, string? connector, string? kind, int? top, CancellationToken ct)
    {
        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        var uri = PowerAutomateEndpointProvider.OperationSearch(target.BaseUri);

        var body = new JsonObject();
        if (!string.IsNullOrWhiteSpace(query))
            body["searchText"] = query;
        if (!string.IsNullOrWhiteSpace(connector))
            body["operationGroupName"] = connector;

        // Deprecated operations are always excluded: surfacing them invites
        // authoring against an operation that no longer works.
        var exclude = new JsonArray("Deprecated");
        switch ((kind ?? "all").ToLowerInvariant())
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

        using var document = await SendAsync(HttpMethod.Post, uri, target, body, ct).ConfigureAwait(false);

        var hits = new List<OperationSearchHit>();
        foreach (var item in EnumerateValue(document.RootElement))
        {
            var name = SwaggerOperationIndexer.TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = SwaggerOperationIndexer.TryGetObject(item, "properties");
            var group = properties is { } p ? SwaggerOperationIndexer.TryGetObject(p, "operationGroup") : null;

            hits.Add(new OperationSearchHit(
                Name: name,
                DisplayName: properties is { } s ? SwaggerOperationIndexer.TryGetString(s, "summary") : null,
                Description: properties is { } d ? SwaggerOperationIndexer.TryGetString(d, "description") : null,
                Usage: properties is { } u ? SwaggerOperationIndexer.TryGetString(u, "usage") : null,
                ConnectorName: group is { } g ? SwaggerOperationIndexer.TryGetString(g, "name") : null,
                ConnectorDisplayName: group is { } gd ? SwaggerOperationIndexer.TryGetString(gd, "displayName") : null));

            if (top is > 0 && hits.Count >= top.Value)
                break;
        }

        return hits;
    }

    public async Task<OperationDetail> GetOperationDetailsAsync(
        string? profileName, string connector, string operation, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        var payload = await GetOperationPayloadAsync(target, connector, operation, ct).ConfigureAwait(false);
        return OperationSchemaReader.Read(connector, operation, payload);
    }

    public async Task<IReadOnlyList<FlowConnectionSummary>> ListConnectionsAsync(
        string? profileName, string? connector, CancellationToken ct)
    {
        // Dataverse connection references are the binding a solution-aware flow
        // actually uses, so they are the primary source. The connectivity API is
        // the fallback: it also reveals connections that exist without a
        // reference, which the caller must create one for before binding.
        var references = await TryListConnectionReferencesAsync(profileName, connector, ct).ConfigureAwait(false);
        if (references.Count > 0)
            return references;

        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        var uri = PowerAutomateEndpointProvider.Connections(target.BaseUri, connector);

        using var document = await SendAsync(HttpMethod.Get, uri, target, null, ct).ConfigureAwait(false);

        var connections = new List<FlowConnectionSummary>();
        foreach (var item in EnumerateValue(document.RootElement))
        {
            var name = SwaggerOperationIndexer.TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = SwaggerOperationIndexer.TryGetObject(item, "properties");
            var api = properties is { } p ? SwaggerOperationIndexer.TryGetObject(p, "api") : null;
            var statuses = properties is { } sp && sp.TryGetProperty("statuses", out var s)
                && s.ValueKind == JsonValueKind.Array
                    ? s
                    : (JsonElement?)null;

            connections.Add(new FlowConnectionSummary(
                ConnectionName: name,
                ConnectorId: api is { } a
                    ? SwaggerOperationIndexer.TryGetString(a, "name") is { } apiName
                        ? PowerAutomateEndpointProvider.BuildApiId(apiName)
                        : SwaggerOperationIndexer.TryGetString(a, "id") ?? string.Empty
                    : string.Empty,
                DisplayName: properties is { } dn ? SwaggerOperationIndexer.TryGetString(dn, "displayName") : null,
                Status: ReadFirstStatus(statuses),
                ConnectionReferenceLogicalName: null,
                Source: "ppapi"));
        }

        return connections;
    }

    public async Task<FlowValidationReport> ValidateDefinitionAsync(
        string? profileName,
        JsonElement document,
        bool connectionCheck,
        bool offline,
        CancellationToken ct)
    {
        IReadOnlyList<FlowConnectionSummary> connections = [];
        if (connectionCheck && !offline)
            connections = await ListConnectionsAsync(profileName, null, ct).ConfigureAwait(false);

        var lookup = offline
            ? null
            : new FlowDefinitionValidator.MetadataLookup(
                (connector, token) => GetConnectorAsync(profileName, connector, null, token),
                (connector, operation, token) => GetOperationDetailsAsync(profileName, connector, operation, token));

        return await FlowDefinitionValidator.ValidateAsync(document, lookup, connectionCheck, connections, ct)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<FlowConnectionSummary>> TryListConnectionReferencesAsync(
        string? profileName, string? connector, CancellationToken ct)
    {
        var filter = "statecode eq 0";
        if (!string.IsNullOrWhiteSpace(connector))
        {
            var apiId = PowerAutomateEndpointProvider.BuildApiId(connector).Replace("'", "''", StringComparison.Ordinal);
            filter += $" and connectorid eq '{apiId}'";
        }

        DataverseQueryResult result;
        try
        {
            result = await _query.QueryODataAsync(
                profileName,
                "connectionreferences",
                "connectionid,connectorid,connectionreferencedisplayname,connectionreferencelogicalname,statecode",
                filter,
                orderBy: null,
                top: null,
                includeAnnotations: false,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // An environment without the connection reference table, or without
            // read access to it, is expected — fall back to the connectivity API.
            _logger.LogDebug(ex, "Could not read Dataverse connection references; falling back to the connectivity API.");
            return [];
        }

        var connections = new List<FlowConnectionSummary>();
        foreach (var record in result.Records)
        {
            var logicalName = SwaggerOperationIndexer.TryGetString(record, "connectionreferencelogicalname");
            if (string.IsNullOrWhiteSpace(logicalName))
                continue;

            connections.Add(new FlowConnectionSummary(
                ConnectionName: SwaggerOperationIndexer.TryGetString(record, "connectionid") ?? logicalName,
                ConnectorId: SwaggerOperationIndexer.TryGetString(record, "connectorid") ?? string.Empty,
                DisplayName: SwaggerOperationIndexer.TryGetString(record, "connectionreferencedisplayname"),
                Status: null,
                ConnectionReferenceLogicalName: logicalName,
                Source: "dataverse"));
        }

        return connections;
    }

    private static string? ReadFirstStatus(JsonElement? statuses)
    {
        if (statuses is not { } array)
            return null;

        foreach (var status in array.EnumerateArray())
        {
            if (SwaggerOperationIndexer.TryGetString(status, "status") is { } value)
                return value;
        }

        return null;
    }

    private async Task<JsonElement> GetConnectorPayloadAsync(
        EnvironmentTarget target, string connector, CancellationToken ct)
    {
        var key = $"{target.EnvironmentId}\0{connector}";
        if (TryGetCached(key, out var cached))
            return cached;

        var uri = PowerAutomateEndpointProvider.Connector(target.BaseUri, connector);
        using var document = await SendAsync(HttpMethod.Get, uri, target, null, ct).ConfigureAwait(false);
        return Cache(key, document.RootElement.Clone());
    }

    private async Task<JsonElement> GetOperationPayloadAsync(
        EnvironmentTarget target, string connector, string operation, CancellationToken ct)
    {
        var key = $"{target.EnvironmentId}\0{connector}\0{operation}";
        if (TryGetCached(key, out var cached))
            return cached;

        var uri = PowerAutomateEndpointProvider.OperationSchema(target.BaseUri, connector, operation);
        using var document = await SendAsync(HttpMethod.Get, uri, target, null, ct).ConfigureAwait(false);
        return Cache(key, document.RootElement.Clone());
    }

    private bool TryGetCached(string key, out JsonElement payload)
    {
        if (_cache.TryGetValue(key, out var entry) && DateTimeOffset.UtcNow < entry.ExpiresAt)
        {
            payload = entry.Payload;
            return true;
        }

        if (entry is not null)
            _cache.TryRemove(key, out _);

        payload = default;
        return false;
    }

    private JsonElement Cache(string key, JsonElement payload)
    {
        _cache[key] = new CacheEntry(payload, DateTimeOffset.UtcNow.Add(CacheLifetime));
        return payload;
    }

    private Task<JsonDocument> SendAsync(
        HttpMethod method, Uri uri, EnvironmentTarget target, JsonNode? body, CancellationToken ct)
        => _client.SendAsync(method, uri, target.Connection, target.Credential, body, ct);

    private static IEnumerable<JsonElement> EnumerateValue(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                : [];
    }

    /// <summary>
    /// Resolves the profile to a connection, credential and per-environment API
    /// host. Mirrors <c>EnvironmentSettingsService.ResolveEnvironmentIdAsync</c>:
    /// the stored environment ID first, the platform catalog second.
    /// </summary>
    private async Task<EnvironmentTarget> ResolveTargetAsync(string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var environmentId = await ResolveEnvironmentIdAsync(ctx, ct).ConfigureAwait(false);
        var baseUri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(
            environmentId, ctx.Connection.Cloud ?? CloudInstance.Public);

        return new EnvironmentTarget(ctx.Connection, ctx.Credential, environmentId, baseUri);
    }

    private async Task<Guid> ResolveEnvironmentIdAsync(ResolvedProfileContext ctx, CancellationToken ct)
    {
        if (ctx.Connection.EnvironmentId.HasValue)
            return ctx.Connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(ctx.Connection.EnvironmentUrl)
            || !Uri.TryCreate(ctx.Connection.EnvironmentUrl, UriKind.Absolute, out var environmentUrl))
        {
            throw new InvalidOperationException(
                $"Connection '{ctx.Connection.Id}' has no EnvironmentId and no usable EnvironmentUrl to resolve one from.");
        }

        _logger.LogDebug(
            "EnvironmentId not stored on connection '{ConnectionId}'. Resolving via the Power Platform catalog...",
            ctx.Connection.Id);

        var environment = await _catalog
            .TryGetByEnvironmentUrlAsync(ctx.Connection, ctx.Credential, environmentUrl, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not resolve a Power Platform environment for '{environmentUrl}'. " +
                "Set the environment ID on the connection, or re-run the connection live check.");

        return environment.EnvironmentId;
    }

    private sealed record CacheEntry(JsonElement Payload, DateTimeOffset ExpiresAt);

    private sealed record EnvironmentTarget(
        Connection Connection, Credential Credential, Guid EnvironmentId, Uri BaseUri);
}
