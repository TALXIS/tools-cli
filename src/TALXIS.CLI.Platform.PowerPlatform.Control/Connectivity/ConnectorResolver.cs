using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// Turns what a person types for a connector into the environment's connector.
/// A custom connector's apiId carries an environment-specific hash, so the
/// stable logical name (Dataverse <c>connector.name</c>) is the canonical
/// input. The rule, in order: the apiId (either form), the logical name, an
/// exact display name.
/// </summary>
internal sealed class ConnectorResolver : IConnectorResolver
{
    private readonly ConnectivityTargetResolver _targets;
    private readonly ConnectorCatalogClient _catalog;
    private readonly ConnectorLogicalNameLookup _logicalNames;

    public ConnectorResolver(
        ConnectivityTargetResolver targets,
        ConnectorCatalogClient catalog,
        ConnectorLogicalNameLookup logicalNames)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logicalNames = logicalNames ?? throw new ArgumentNullException(nameof(logicalNames));
    }

    public async Task<ResolvedConnectorInfo> ResolveAsync(string? profileName, string connector, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        return await ResolveAsync(target, connector, ct).ConfigureAwait(false);
    }

    /// <summary>Resolves against a target that is already known.</summary>
    public async Task<ResolvedConnectorInfo> ResolveAsync(ConnectivityTarget target, string connector, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);

        var input = connector.Trim();
        var shortId = ConnectorIds.ToShort(input);
        var catalog = await _catalog.GetAsync(target, ct).ConfigureAwait(false);

        var byApiId = FindByApiId(catalog, shortId);
        if (byApiId is not null)
            return await ToInfoAsync(target, byApiId, ct).ConfigureAwait(false);

        var apiId = await _logicalNames.TryFindApiIdAsync(target.ProfileName, shortId, ct).ConfigureAwait(false);
        if (apiId is not null)
        {
            return FindByApiId(catalog, apiId) is { } known
                ? ToInfo(known, shortId)
                : new ResolvedConnectorInfo(apiId, shortId, null, true, null);
        }

        return await ToInfoAsync(target, FindByDisplayName(catalog, input), ct).ConfigureAwait(false);
    }

    // A custom connector's logical name exists only in Dataverse, so it is looked up
    // when the caller named the connector some other way.
    private async Task<ResolvedConnectorInfo> ToInfoAsync(ConnectivityTarget target, CatalogConnector connector, CancellationToken ct)
    {
        if (!connector.IsCustom)
            return ToInfo(connector, null);

        var logicalNames = await _logicalNames.ReadAllAsync(target.ProfileName, ct).ConfigureAwait(false);
        return ToInfo(connector, logicalNames.LogicalNameOf(connector));
    }

    private static CatalogConnector? FindByApiId(IReadOnlyList<CatalogConnector> catalog, string apiId)
        => catalog.FirstOrDefault(c => string.Equals(c.ApiId, apiId, StringComparison.OrdinalIgnoreCase));

    private static CatalogConnector FindByDisplayName(IReadOnlyList<CatalogConnector> catalog, string displayName)
    {
        var matches = catalog
            .Where(c => string.Equals(c.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ConnectorNotFoundException(displayName),
            _ => throw new AmbiguousConnectorException(displayName, matches.Select(c => c.ApiId).ToList()),
        };
    }

    private static ResolvedConnectorInfo ToInfo(CatalogConnector connector, string? logicalName)
        => new(connector.ApiId, logicalName ?? connector.ApiId, connector.DisplayName, connector.IsCustom, connector.XrmConnectorId);
}
