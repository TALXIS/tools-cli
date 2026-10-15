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
internal sealed class PowerAutomateConnectorService : IPowerAutomateConnectorService
{
    /// <summary>
    /// Connector and operation payloads are stable across an authoring session
    /// and each fetch costs a round trip, so they are cached briefly. The
    /// validator hits the same operations repeatedly while walking a flow.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly IConfigurationResolver _resolver;
    private readonly IEnvironmentIdResolver _environmentIds;
    private readonly IConnectorResolver _connectors;
    private readonly IDataverseQueryService _query;
    private readonly PowerAutomateApiClient _client;
    private readonly ILogger<PowerAutomateConnectorService> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public PowerAutomateConnectorService(
        IConfigurationResolver resolver,
        IEnvironmentIdResolver environmentIds,
        IConnectorResolver connectors,
        IDataverseQueryService query,
        PowerAutomateApiClient client,
        ILogger<PowerAutomateConnectorService>? logger = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _environmentIds = environmentIds ?? throw new ArgumentNullException(nameof(environmentIds));
        _connectors = connectors ?? throw new ArgumentNullException(nameof(connectors));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? NullLogger<PowerAutomateConnectorService>.Instance;
    }

    public async Task<ConnectorDetail> GetConnectorAsync(
        string? profileName, string connector, string? query, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);

        var resolved = await _connectors.ResolveAsync(profileName, connector, ct).ConfigureAwait(false);
        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        var payload = await GetConnectorPayloadAsync(target, resolved.ApiId, ct).ConfigureAwait(false);
        return SwaggerOperationIndexer.Index(payload, resolved, query);
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
        {
            var resolved = await _connectors.ResolveAsync(profileName, connector, ct).ConfigureAwait(false);
            body["operationGroupName"] = resolved.ApiId;
        }

        // Deprecated operations are always excluded: surfacing them invites
        // authoring against an operation that no longer works.
        var exclude = new JsonArray("Deprecated");
        switch ((kind ?? OperationKinds.All).ToLowerInvariant())
        {
            case OperationKinds.Actions:
                body["allTagsToInclude"] = new JsonArray("Action");
                exclude.Add("Trigger");
                break;
            case OperationKinds.Triggers:
                body["allTagsToInclude"] = new JsonArray("Trigger");
                break;
            case OperationKinds.All:
                break;
            default:
                throw new ArgumentException(
                    $"Unknown operation kind '{kind}'. Use '{OperationKinds.All}', '{OperationKinds.Actions}', or '{OperationKinds.Triggers}'.", nameof(kind));
        }

        body["anyTagsToExclude"] = exclude;

        using var document = await SendAsync(HttpMethod.Post, uri, target, body, ct).ConfigureAwait(false);

        var hits = new List<OperationSearchHit>();
        foreach (var item in EnumerateValue(document.RootElement))
        {
            var name = JsonElementReader.TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = JsonElementReader.TryGetObject(item, "properties");
            var group = properties is { } p ? JsonElementReader.TryGetObject(p, "operationGroup") : null;

            hits.Add(new OperationSearchHit(
                Name: name,
                DisplayName: properties is { } s ? JsonElementReader.TryGetString(s, "summary") : null,
                Description: properties is { } d ? JsonElementReader.TryGetString(d, "description") : null,
                Usage: properties is { } u ? JsonElementReader.TryGetString(u, "usage") : null,
                ConnectorName: group is { } g ? JsonElementReader.TryGetString(g, "name") : null,
                ConnectorDisplayName: group is { } gd ? JsonElementReader.TryGetString(gd, "displayName") : null));

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

        var resolved = await _connectors.ResolveAsync(profileName, connector, ct).ConfigureAwait(false);
        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        var payload = await GetOperationPayloadAsync(target, resolved.ApiId, operation, ct).ConfigureAwait(false);
        return OperationSchemaReader.Read(resolved, operation, payload);
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
            var name = JsonElementReader.TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = JsonElementReader.TryGetObject(item, "properties");
            var api = properties is { } p ? JsonElementReader.TryGetObject(p, "api") : null;
            var statuses = properties is { } sp && sp.TryGetProperty("statuses", out var s)
                && s.ValueKind == JsonValueKind.Array
                    ? s
                    : (JsonElement?)null;

            connections.Add(new FlowConnectionSummary(
                ConnectionName: name,
                ConnectorId: api is { } a
                    ? JsonElementReader.TryGetString(a, "name") is { } apiName
                        ? PowerAutomateEndpointProvider.BuildApiId(apiName)
                        : JsonElementReader.TryGetString(a, "id") ?? string.Empty
                    : string.Empty,
                DisplayName: properties is { } dn ? JsonElementReader.TryGetString(dn, "displayName") : null,
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
            var logicalName = JsonElementReader.TryGetString(record, "connectionreferencelogicalname");
            if (string.IsNullOrWhiteSpace(logicalName))
                continue;

            connections.Add(new FlowConnectionSummary(
                ConnectionName: JsonElementReader.TryGetString(record, "connectionid") ?? logicalName,
                ConnectorId: JsonElementReader.TryGetString(record, "connectorid") ?? string.Empty,
                DisplayName: JsonElementReader.TryGetString(record, "connectionreferencedisplayname"),
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
            if (JsonElementReader.TryGetString(status, "status") is { } value)
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

    private async Task<EnvironmentTarget> ResolveTargetAsync(string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var environmentId = await _environmentIds.ResolveAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);
        var baseUri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(
            environmentId, ctx.Connection.Cloud ?? CloudInstance.Public);

        return new EnvironmentTarget(ctx.Connection, ctx.Credential, environmentId, baseUri);
    }

    private sealed record CacheEntry(JsonElement Payload, DateTimeOffset ExpiresAt);

    private sealed record EnvironmentTarget(
        Connection Connection, Credential Credential, Guid EnvironmentId, Uri BaseUri);
}
