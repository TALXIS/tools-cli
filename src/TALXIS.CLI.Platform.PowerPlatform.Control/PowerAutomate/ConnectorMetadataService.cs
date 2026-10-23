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

    public async Task<OperationResolution> ResolveOperationAsync(
        string? profileName,
        string connector,
        string operation,
        string connectionReference,
        string connectionId,
        IReadOnlyDictionary<string, string> values,
        string? parameter,
        string? output,
        bool useConnection,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(values);

        var target = await ResolveTargetAsync(profileName, ct).ConfigureAwait(false);

        // Metadata only so far: none of these calls go through the connection.
        OperationDetail detail;
        using (var document = await SendForLookupAsync(
            PowerAutomateEndpointProvider.Operation(target.BaseUri, connector, operation), target.Token,
            $"Operation '{operation}' does not exist on connector '{connector}'. List its operations with 'environment connector get {connector}'.",
            ct).ConfigureAwait(false))
        {
            detail = OperationSchemaReader.Read(connector, operation, document.RootElement);
        }

        using var connectorDocument = await SendForLookupAsync(
            PowerAutomateEndpointProvider.Connector(target.BaseUri, connector), target.Token,
            $"Connector '{connector}' does not exist in this environment.",
            ct).ConfigureAwait(false);

        ConnectionDetail connection;
        using (var document = await SendForLookupAsync(
            PowerAutomateEndpointProvider.Connection(target.Cloud, target.EnvironmentId, connector, connectionId), target.Token,
            $"Connection '{connectionId}' behind '{connectionReference}' does not exist or is not visible to you.",
            ct).ConfigureAwait(false))
        {
            connection = ReadConnection(connectionId, document.RootElement);
        }

        var (lookupSource, lookupValues) = await ResolveLookupSourceAsync(
            target, connector, operation, detail, values, ct).ConfigureAwait(false);
        // Outputs are planned the same way as parameters: a dynamic output's
        // fields come from a lookup just like a dynamic input's.
        var resolvingOutputs = output is not null;
        var targets = resolvingOutputs ? detail.Outputs ?? [] : detail.Parameters;
        var source = resolvingOutputs ? lookupSource with { Parameters = lookupSource.Outputs ?? [] } : lookupSource;
        var plan = DynamicLookupPlanner.Plan(source, connectorDocument.RootElement, connectionId, lookupValues)
            .Where(p => targets.Any(d => d.Name == p.Parameter.Name))
            .Where(p => (output ?? parameter) is not { } only || p.Parameter.Name == only)
            .ToList();
        if (!useConnection)
            return new OperationResolution(detail, connectionReference, connection, false, plan.Select(p => p.Lookup).ToList());

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

            try
            {
                using var response = await _client.SendAsync(
                        planned.Method ?? HttpMethod.Get, planned.RequestUri, runtimeToken, planned.Body, ct, planned.Headers)
                    .ConfigureAwait(false);
                parameters = DynamicLookupPlanner.Apply(parameters, planned, response.RootElement, resolvingOutputs);
                lookups.Add(planned.Lookup with { Status = DynamicLookupStatus.Resolved });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failed lookup (e.g. a choice the connection cannot see)
                // must not hide the ones that did resolve.
                _logger.LogDebug(ex, "Dynamic lookup {Operation} failed.", planned.Lookup.OperationId);
                lookups.Add(planned.Lookup with { Status = DynamicLookupStatus.Failed, Reason = ex.Message });
            }
        }

        var resolved = resolvingOutputs ? detail with { Outputs = parameters } : detail with { Parameters = parameters };
        return new OperationResolution(resolved, connectionReference, connection, true, lookups);
    }

    /// <summary>
    /// The operation whose lookups to plan, and the values to plan them with.
    /// Usually the operation itself; see
    /// <see cref="PowerAutomateEndpointProvider.EnvironmentScopedTwin"/> for the
    /// exception, where the twin's lookups are used and the environment is the
    /// profile's own.
    /// </summary>
    private async Task<(OperationDetail Source, IReadOnlyDictionary<string, string> Values)> ResolveLookupSourceAsync(
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
            return (detail, values);
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
            return (OperationSchemaReader.Read(connector, twin.Operation, document.RootElement), withEnvironment);
        }
        catch (ArgumentException ex)
        {
            // No twin for this operation: plan its own lookups.
            _logger.LogDebug(ex, "No {Twin} twin for {Operation}.", twin.Operation, operation);
            return (detail, values);
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
        var properties = JsonRead.Object(payload, "properties") ?? default;
        var owner = JsonRead.Object(properties, "createdBy") ?? default;
        string? status = null;
        if (properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("statuses", out var statuses)
            && statuses.ValueKind == JsonValueKind.Array)
        {
            status = statuses.EnumerateArray().Select(e => JsonRead.String(e, "status")).FirstOrDefault(v => v is not null);
        }

        return new ConnectionDetail(
            ConnectionId: connectionId,
            DisplayName: JsonRead.String(properties, "displayName"),
            OwnerName: JsonRead.String(owner, "displayName"),
            OwnerPrincipalName: JsonRead.String(owner, "userPrincipalName") ?? JsonRead.String(owner, "email"),
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
        var cloud = ctx.Connection.Cloud ?? CloudInstance.Public;
        var token = await _client.AcquireTokenAsync(ctx.Connection, ctx.Credential, ct).ConfigureAwait(false);

        return new EnvironmentTarget(baseUri, token, environmentId, cloud, ctx.Connection, ctx.Credential);
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

    private sealed record EnvironmentTarget(
        Uri BaseUri, string Token, Guid EnvironmentId, CloudInstance Cloud, Connection Connection, Credential Credential);
}
