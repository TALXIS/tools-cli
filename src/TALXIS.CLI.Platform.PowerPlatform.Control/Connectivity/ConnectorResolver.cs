using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>A connector the environment knows, as the connectivity API names it.</summary>
/// <param name="ApiId">Short connector id the connectivity routes take.</param>
/// <param name="LogicalName">
/// Solution-aware logical name of a custom connector (stable across
/// environments); for a built-in connector the logical name is the apiId.
/// </param>
internal sealed record ResolvedConnector(string ApiId, string LogicalName, string? DisplayName, bool IsCustom);

/// <summary>
/// Turns what a person types for <c>--connector</c> into the environment's
/// apiId. A custom connector's apiId carries an environment-specific hash, so
/// the stable logical name (Dataverse <c>connector.name</c>) is the canonical
/// input; the apiId in either form and an exact display name also work.
/// </summary>
internal sealed class ConnectorResolver
{
    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromMinutes(5);

    private readonly ConnectivityApiClient _client;
    private readonly IDataverseQueryService _query;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, CatalogEntry> _catalogs = new();

    public ConnectorResolver(ConnectivityApiClient client, IDataverseQueryService query, ILogger logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <exception cref="ArgumentException">Unknown or ambiguous connector.</exception>
    public async Task<ResolvedConnector> ResolveAsync(ConnectivityTarget target, string connector, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);

        var input = connector.Trim();
        var shortId = ConnectivityEndpoints.ToShortApiId(input);
        var catalog = await GetCatalogAsync(target, ct).ConfigureAwait(false);

        // 1. The apiId itself (short or path form).
        var byId = catalog.FirstOrDefault(c => string.Equals(c.ApiId, shortId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
            return ToResolved(byId);

        // 2. The logical name of a custom connector, through Dataverse.
        var apiId = await TryResolveLogicalNameAsync(target, shortId, ct).ConfigureAwait(false);
        if (apiId is not null)
        {
            var known = catalog.FirstOrDefault(c => string.Equals(c.ApiId, apiId, StringComparison.OrdinalIgnoreCase));
            return known is not null
                ? ToResolved(known) with { LogicalName = shortId }
                : new ResolvedConnector(apiId, shortId, null, true);
        }

        // 3. An exact display name.
        var byName = catalog
            .Where(c => string.Equals(c.DisplayName, input, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byName.Count == 1)
            return ToResolved(byName[0]);
        if (byName.Count > 1)
        {
            throw new ArgumentException(
                $"The display name '{input}' matches {byName.Count} connectors: " +
                string.Join(", ", byName.Select(c => c.ApiId)) +
                ". Use the connector's logical name or apiId.");
        }

        throw new ArgumentException(
            $"Connector '{input}' was not found in this environment. Use 'txc environment connector list' to see what is available.");
    }

    private static ResolvedConnector ToResolved(CatalogConnector c)
        => new(c.ApiId, c.ApiId, c.DisplayName, c.IsCustom);

    private async Task<string?> TryResolveLogicalNameAsync(ConnectivityTarget target, string logicalName, CancellationToken ct)
    {
        try
        {
            var escaped = logicalName.Replace("'", "''", StringComparison.Ordinal);
            var result = await _query.QueryODataAsync(
                target.ProfileName,
                "connectors",
                "connectorid,name,connectorinternalid",
                $"name eq '{escaped}'",
                orderBy: null,
                top: 2,
                includeAnnotations: false,
                ct).ConfigureAwait(false);

            foreach (var record in result.Records)
            {
                if (record.ValueKind == JsonValueKind.Object
                    && record.TryGetProperty("connectorinternalid", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(id.GetString()))
                {
                    return id.GetString();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A profile without Dataverse access, or an environment without the
            // connector table, simply has no logical names to resolve.
            _logger.LogDebug(ex, "Could not look up connector logical name '{LogicalName}' in Dataverse.", logicalName);
        }

        return null;
    }

    private async Task<IReadOnlyList<CatalogConnector>> GetCatalogAsync(ConnectivityTarget target, CancellationToken ct)
    {
        if (_catalogs.TryGetValue(target.EnvironmentId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Connectors;

        using var response = await _client.SendAsync(
            HttpMethod.Get,
            ConnectivityEndpoints.Connectors(target.BaseUri, target.EnvironmentId),
            target.Connection,
            target.Credential,
            null,
            ct).ConfigureAwait(false);

        var connectors = new List<CatalogConnector>();
        if (response.Body is { } doc
            && doc.RootElement.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var props = item.TryGetProperty("properties", out var p) ? p : default;
                connectors.Add(new CatalogConnector(
                    name,
                    GetString(props, "displayName"),
                    props.ValueKind == JsonValueKind.Object
                        && props.TryGetProperty("isCustomApi", out var custom)
                        && custom.ValueKind == JsonValueKind.True));
            }
        }

        _catalogs[target.EnvironmentId] = new CatalogEntry(connectors, DateTimeOffset.UtcNow + CatalogLifetime);
        return connectors;
    }

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

    private sealed record CatalogConnector(string ApiId, string? DisplayName, bool IsCustom);

    private sealed record CatalogEntry(IReadOnlyList<CatalogConnector> Connectors, DateTimeOffset ExpiresAt);
}
