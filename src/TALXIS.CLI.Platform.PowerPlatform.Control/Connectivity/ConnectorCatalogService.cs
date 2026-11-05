using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>Lists the connectors of the environment, each with its logical name.</summary>
internal sealed class ConnectorCatalogService : IConnectorService
{
    private readonly ConnectivityTargetResolver _targets;
    private readonly ConnectorCatalogClient _catalog;
    private readonly ConnectorLogicalNameLookup _logicalNames;

    public ConnectorCatalogService(
        ConnectivityTargetResolver targets,
        ConnectorCatalogClient catalog,
        ConnectorLogicalNameLookup logicalNames)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logicalNames = logicalNames ?? throw new ArgumentNullException(nameof(logicalNames));
    }

    public async Task<IReadOnlyList<ConnectorListItem>> ListAsync(
        string? profileName, string? filter, string? kind, int? top, CancellationToken ct)
    {
        if (kind is not null && !ConnectorKind.IsKnown(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind), kind, $"Unknown connector kind '{kind}'. Use {ConnectorKind.BuiltIn} or {ConnectorKind.Custom}.");
        }

        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var catalog = await _catalog.GetAsync(target, ct).ConfigureAwait(false);
        var names = catalog.Any(c => c.IsCustom)
            ? await _logicalNames.ReadAllAsync(profileName, ct).ConfigureAwait(false)
            : ConnectorLogicalNames.None;

        var items = catalog
            .Where(c => kind is null || ConnectorKind.Of(c.IsCustom) == kind)
            .Select(c => new ConnectorListItem(
                names.LogicalNameOf(c), c.ApiId, c.DisplayName, ConnectorKind.Of(c.IsCustom), c.Tier, c.Description))
            .Where(i => Matches(i, filter))
            .OrderBy(i => i.LogicalName, StringComparer.OrdinalIgnoreCase);

        return (top is > 0 ? items.Take(top.Value) : items).ToList();
    }

    private static bool Matches(ConnectorListItem item, string? filter)
        => string.IsNullOrWhiteSpace(filter)
            || item.LogicalName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || item.ApiId.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (item.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
}
