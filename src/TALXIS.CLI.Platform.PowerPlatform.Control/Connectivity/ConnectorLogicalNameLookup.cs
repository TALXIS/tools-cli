using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>The logical names of the environment's custom connectors, by Dataverse row id and by apiId.</summary>
internal sealed record ConnectorLogicalNames(
    IReadOnlyDictionary<Guid, string> ByRowId,
    IReadOnlyDictionary<string, string> ByApiId)
{
    public static ConnectorLogicalNames None { get; } = new(
        new Dictionary<Guid, string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The apiId for a built-in connector, and for a custom one whose logical name is not known.</summary>
    public string LogicalNameOf(CatalogConnector connector)
    {
        if (!connector.IsCustom)
            return connector.ApiId;
        if (connector.XrmConnectorId is { } rowId && ByRowId.TryGetValue(rowId, out var byRow))
            return byRow;
        return ByApiId.TryGetValue(connector.ApiId, out var byApi) ? byApi : connector.ApiId;
    }
}

/// <summary>
/// The logical names of custom connectors, from <see cref="ICustomConnectorNameSource"/>
/// (they live in Dataverse, which the control plane does not query itself).
/// </summary>
internal sealed class ConnectorLogicalNameLookup
{
    private readonly ICustomConnectorNameSource _names;

    public ConnectorLogicalNameLookup(ICustomConnectorNameSource names)
        => _names = names ?? throw new ArgumentNullException(nameof(names));

    /// <summary>The apiId of the custom connector with this logical name, or null when there is none or Dataverse cannot say.</summary>
    public Task<string?> TryFindApiIdAsync(string? profileName, string logicalName, CancellationToken ct)
        => _names.TryFindApiIdAsync(profileName, logicalName, ct);

    public async Task<ConnectorLogicalNames> ReadAllAsync(string? profileName, CancellationToken ct)
    {
        var byRow = new Dictionary<Guid, string>();
        var byApi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in await _names.ReadAllAsync(profileName, ct).ConfigureAwait(false))
        {
            if (name.ConnectorRowId is { } rowId)
                byRow[rowId] = name.LogicalName;
            if (name.ApiId is { } apiId)
                byApi[apiId] = name.LogicalName;
        }

        return new ConnectorLogicalNames(byRow, byApi);
    }
}
