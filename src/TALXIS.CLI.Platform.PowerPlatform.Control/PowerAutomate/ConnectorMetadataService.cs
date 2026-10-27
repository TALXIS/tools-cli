using System.Collections.Concurrent;
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
internal sealed class ConnectorMetadataService : IConnectorMetadataService
{
    private readonly IConfigurationResolver _resolver;
    private readonly IEnvironmentIdResolver _environmentIds;
    private readonly PowerAutomateApiClient _client;
    private readonly ILogger<ConnectorMetadataService> _logger;
    private readonly ConcurrentDictionary<string, Task<ResolvedProfile>> _profiles = new(StringComparer.Ordinal);

    public ConnectorMetadataService(
        IConfigurationResolver resolver,
        IEnvironmentIdResolver environmentIds,
        PowerAutomateApiClient client,
        ILogger<ConnectorMetadataService>? logger = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _environmentIds = environmentIds ?? throw new ArgumentNullException(nameof(environmentIds));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? NullLogger<ConnectorMetadataService>.Instance;
    }

    public async Task<IReadOnlyList<ConnectorSummary>> ListConnectorsAsync(
        string? profileName, string? search, CancellationToken ct)
    {
        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        using var document = await _client.SendAsync(
            HttpMethod.Get, PowerAutomateEndpointProvider.Connectors(target.BaseUri), target.Token, null, ct)
            .ConfigureAwait(false);

        var connectors = new List<ConnectorSummary>();
        foreach (var item in ODataItems(document.RootElement))
        {
            var name = JsonElementReader.TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = JsonElementReader.TryGetObject(item, "properties") ?? default;
            var connector = new ConnectorSummary(
                Name: name,
                DisplayName: JsonElementReader.TryGetString(properties, "displayName"),
                Description: JsonElementReader.TryGetString(properties, "description"),
                Tier: JsonElementReader.TryGetString(properties, "tier"));

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
            $"Connector '{connector}' does not exist in this environment. Find the connector name with 'environment connector list --filter <text>'.",
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
            case "action":
                body["allTagsToInclude"] = new JsonArray("Action");
                exclude.Add("Trigger");
                break;
            case "trigger":
                body["allTagsToInclude"] = new JsonArray("Trigger");
                break;
            case "all":
                break;
            default:
                throw new ArgumentException(
                    $"Unknown operation kind '{kind}'. Use 'all', 'action', or 'trigger'.", nameof(kind));
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

    public async Task<OperationResolution> ResolveOperationAsync(
        string? profileName, OperationResolutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentNullException.ThrowIfNull(request.Values);

        var detail = request.Operation;
        var (connector, operation, name) = (detail.Connector, detail.OperationId, request.Name);
        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);

        // Metadata only so far: none of these calls go through the connection.
        using var connectorDocument = await SendForLookupAsync(
            PowerAutomateEndpointProvider.Connector(target.BaseUri, connector), target.Token,
            $"Connector '{connector}' does not exist in this environment.",
            ct).ConfigureAwait(false);

        var (lookupSource, lookupValues, unavailable) = await ResolveLookupSourceAsync(
            target, connector, operation, detail, request.Values, ct).ConfigureAwait(false);
        if (unavailable is not null)
        {
            // Its own lookups are known not to answer outside the designer.
            var asked = (request.IsOutput ? detail.Outputs ?? [] : detail.Parameters).FirstOrDefault(p => p.Name == name);
            var kind = asked is { DynamicValues: null, DynamicSchema: not null } ? DynamicLookupPlanner.SchemaKind : DynamicLookupPlanner.ValuesKind;
            return new OperationResolution(detail, false,
                [new DynamicLookup(name, kind, operation, DynamicLookupStatus.Skipped, unavailable)]);
        }

        // Outputs are planned the same way as parameters: a dynamic output's
        // fields come from a lookup just like a dynamic input's.
        var resolvingOutputs = request.IsOutput;
        var targets = resolvingOutputs ? detail.Outputs ?? [] : detail.Parameters;
        var source = resolvingOutputs ? lookupSource with { Parameters = lookupSource.Outputs ?? [] } : lookupSource;
        var plan = DynamicLookupPlanner.Plan(source, connectorDocument.RootElement, request.ConnectionId, lookupValues)
            .Where(p => p.Parameter.Name == name)
            .ToList();
        if (!request.UseConnection)
            return new OperationResolution(detail, false, plan.Select(p => p.Lookup).ToList());

        var runtimeToken = await _client.AcquireRuntimeTokenAsync(target.Connection, target.Credential, ct).ConfigureAwait(false);
        var parameters = targets;
        var lookups = new List<DynamicLookup>();

        foreach (var planned in plan)
        {
            if (planned.RequestUri is null)
            {
                lookups.Add(planned.Lookup);
                continue;
            }

            // A failed lookup fails the command: it is the one answer asked for.
            using var response = await _client.SendAsync(
                    planned.Method ?? HttpMethod.Get, planned.RequestUri, runtimeToken, planned.Body, ct, planned.Headers)
                .ConfigureAwait(false);
            parameters = DynamicLookupPlanner.Apply(parameters, planned, response.RootElement, resolvingOutputs);
            lookups.Add(planned.Lookup with { Status = DynamicLookupStatus.Resolved });
        }

        var resolved = resolvingOutputs ? detail with { Outputs = parameters } : detail with { Parameters = parameters };
        return new OperationResolution(resolved, true, lookups);
    }

    /// <summary>
    /// The operation whose lookups to plan, and the values to plan them with.
    /// Usually the operation itself; see
    /// <see cref="PowerAutomateEndpointProvider.EnvironmentScopedTwin"/> for the
    /// exception, where the twin's lookups are used and the environment is the
    /// profile's own. <c>Unavailable</c> says why nothing can be looked up when
    /// such an operation has no twin.
    /// </summary>
    private async Task<(OperationDetail Source, IReadOnlyDictionary<string, string> Values, string? Unavailable)> ResolveLookupSourceAsync(
        EnvironmentTarget target,
        string connector,
        string operation,
        OperationDetail detail,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct)
    {
        if (PowerAutomateEndpointProvider.EnvironmentScopedTwin(connector, operation) is not { } twin
            || string.IsNullOrWhiteSpace(target.Connection.EnvironmentUrl))
        {
            return (detail, values, null);
        }

        try
        {
            using var document = await _client.SendAsync(
                HttpMethod.Get, PowerAutomateEndpointProvider.Operation(target.BaseUri, connector, twin.Operation), target.Token, null, ct)
                .ConfigureAwait(false);

            var withEnvironment = new Dictionary<string, string>(values, StringComparer.Ordinal)
            {
                [twin.EnvironmentParameter] = target.Connection.EnvironmentUrl.TrimEnd('/'),
            };
            return (OperationSchemaReader.Read(connector, twin.Operation, document.RootElement), withEnvironment, null);
        }
        catch (ArgumentException ex)
        {
            _logger.LogDebug(ex, "No {Twin} twin for {Operation}.", twin.Operation, operation);
            return (detail, values,
                $"'{operation}' has no '{twin.Operation}' counterpart, and its own lookups only answer inside the flow designer. Choose the value with the user.");
        }
    }

    public async Task<ConnectionDetail> GetConnectionAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);
        using var document = await SendForLookupAsync(
            PowerAutomateEndpointProvider.Connection(target.Cloud, target.EnvironmentId, connector, connectionId), target.Token,
            $"Connection '{connectionId}' does not exist or is not visible to you.",
            ct).ConfigureAwait(false);
        return ReadConnection(connectionId, document.RootElement);
    }

    private static ConnectionDetail ReadConnection(string connectionId, JsonElement payload)
    {
        var properties = JsonElementReader.TryGetObject(payload, "properties") ?? default;
        var owner = JsonElementReader.TryGetObject(properties, "createdBy") ?? default;
        string? status = null;
        if (properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("statuses", out var statuses)
            && statuses.ValueKind == JsonValueKind.Array)
        {
            status = statuses.EnumerateArray().Select(e => JsonElementReader.TryGetString(e, "status")).FirstOrDefault(v => v is not null);
        }

        return new ConnectionDetail(
            ConnectionId: connectionId,
            OwnerName: JsonElementReader.TryGetString(owner, "displayName"),
            OwnerPrincipalName: JsonElementReader.TryGetString(owner, "userPrincipalName") ?? JsonElementReader.TryGetString(owner, "email"),
            Status: status);
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
        foreach (var item in ODataItems(root))
        {
            var name = JsonElementReader.TryGetString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var properties = JsonElementReader.TryGetObject(item, "properties") ?? default;
            var summary = JsonElementReader.TryGetString(properties, "summary");
            if (IsDeprecatedHit(properties, summary))
                continue;

            var group = JsonElementReader.TryGetObject(properties, "operationGroup") ?? default;
            hits.Add(new OperationSearchHit(
                Name: name,
                DisplayName: summary,
                Description: JsonElementReader.TryGetString(properties, "description"),
                ConnectorName: JsonElementReader.TryGetString(group, "name"),
                ConnectorDisplayName: JsonElementReader.TryGetString(group, "displayName")));

            if (top > 0 && hits.Count >= top)
                break;
        }

        return hits;
    }

    private static bool IsDeprecatedHit(JsonElement properties, string? summary)
        => OperationSchemaReader.IsDeprecated(properties)
            || (summary?.Contains("(deprecated)", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Items of a bare array, or of the <c>value</c> array of an OData envelope.</summary>
    private static IEnumerable<JsonElement> ODataItems(JsonElement root)
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
    /// The profile's per-environment API host and a token. The profile is
    /// resolved once per profile name: a command calls this service several
    /// times (operation, connection, lookup) and should resolve, log and look
    /// up its environment id a single time. Each CLI command, including each
    /// MCP tool call, runs in its own process, so this never outlives one
    /// command. Tokens are not kept here; the token service caches them.
    /// </summary>
    private async Task<EnvironmentTarget> ResolveTargetAsync(string? profileName, CancellationToken ct)
    {
        var profile = await _profiles.GetOrAdd(profileName ?? string.Empty, _ => ResolveProfileAsync(profileName, ct))
            .ConfigureAwait(false);
        var token = await _client.AcquireTokenAsync(profile.Connection, profile.Credential, ct).ConfigureAwait(false);

        return new EnvironmentTarget(profile.BaseUri, token, profile.EnvironmentId, profile.Cloud, profile.Connection, profile.Credential);
    }

    private async Task<ResolvedProfile> ResolveProfileAsync(string? profileName, CancellationToken ct)
    {
        var ctx = await _resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var environmentId = await _environmentIds.ResolveAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);
        var cloud = ctx.Connection.Cloud ?? CloudInstance.Public;
        var baseUri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(environmentId, cloud);

        return new ResolvedProfile(baseUri, environmentId, cloud, ctx.Connection, ctx.Credential);
    }

    private sealed record ResolvedProfile(
        Uri BaseUri, Guid EnvironmentId, CloudInstance Cloud, Connection Connection, Credential Credential);

    private sealed record EnvironmentTarget(
        Uri BaseUri, string Token, Guid EnvironmentId, CloudInstance Cloud, Connection Connection, Credential Credential);
}
