using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// Connector metadata from the most local source that has it, in this order:
/// <list type="number">
/// <item>a custom connector defined in the workspace, read from its own
/// definition file (never cached: the file is the source of truth);</item>
/// <item>a snapshot in the workspace's <c>.txc/connectors/</c>, written the
/// first time the environment answered (<see cref="ConnectorSnapshotStore"/>);</item>
/// <item>the connected environment, whose answer is then snapshotted.</item>
/// </list>
/// Outside a workspace nothing is read or written locally and the environment
/// answers every time. <c>refresh</c> skips the snapshot and rewrites it.
/// </summary>
internal static class ConnectorMetadataLookup
{
    public static async Task<ConnectorDetail> GetConnectorAsync(string? profile, string connector, string? search, CancellationToken ct, bool refresh = false)
    {
        var workspace = FindWorkspace();
        if (workspace is not null && InWorkspace(workspace, connector) is { } local)
        {
            var detail = TxcServices.Get<IConnectorDefinitionReader>()
                .ReadConnector(await ReadDefinitionAsync(local, ct).ConfigureAwait(false), local.Connector.Identifier, search);
            return detail with { Source = ConnectorMetadataSource.Workspace, SourcePath = local.RelativeDefinitionPath };
        }

        var metadata = TxcServices.Get<IConnectorMetadataService>();
        if (workspace is null)
        {
            var remote = await metadata.GetConnectorAsync(profile, connector, search, ct).ConfigureAwait(false);
            return remote with { Source = ConnectorMetadataSource.Environment };
        }

        var store = new ConnectorSnapshotStore(workspace);
        if (!refresh && await store.ReadConnectorAsync(connector, ct).ConfigureAwait(false) is { } snapshot)
            return Filter(snapshot, search);

        // The index is stored whole so any later filter can be answered from it.
        var fetched = await metadata.GetConnectorAsync(profile, connector, search: null, ct).ConfigureAwait(false);
        var path = await store.WriteConnectorAsync(fetched, Now(), ct).ConfigureAwait(false);
        return Filter(fetched, search) with { Source = ConnectorMetadataSource.Environment, SourcePath = path };
    }

    public static async Task<OperationDetail> GetOperationAsync(string? profile, string connector, string operation, CancellationToken ct, bool refresh = false)
    {
        var workspace = FindWorkspace();
        if (workspace is not null && InWorkspace(workspace, connector) is { } local)
        {
            var detail = TxcServices.Get<IConnectorDefinitionReader>()
                .ReadOperation(await ReadDefinitionAsync(local, ct).ConfigureAwait(false), local.Connector.Identifier, operation);
            return detail with { Source = ConnectorMetadataSource.Workspace, SourcePath = local.RelativeDefinitionPath };
        }

        if (workspace is null)
            return await GetFromEnvironmentAsync(profile, connector, operation, ct).ConfigureAwait(false);

        var store = new ConnectorSnapshotStore(workspace);
        if (!refresh && await store.ReadOperationAsync(connector, operation, ct).ConfigureAwait(false) is { } snapshot)
            return snapshot;

        var fetched = await GetFromEnvironmentAsync(profile, connector, operation, ct).ConfigureAwait(false);
        var path = await store.WriteOperationAsync(fetched, Now(), ct).ConfigureAwait(false);
        return fetched with { SourcePath = path };
    }

    /// <summary>
    /// The environment's copy of an operation, for what only the deployed
    /// connector can answer: a lookup through a connection needs its runtime.
    /// </summary>
    public static async Task<OperationDetail> GetFromEnvironmentAsync(string? profile, string connector, string operation, CancellationToken ct)
    {
        var remote = await TxcServices.Get<IConnectorMetadataService>().GetOperationAsync(profile, connector, operation, ct).ConfigureAwait(false);
        return remote with { Source = ConnectorMetadataSource.Environment };
    }

    /// <summary>The workspace connector <paramref name="connector"/> names, if the workspace defines it.</summary>
    private static LocalConnector? InWorkspace(string root, string connector)
        => WorkspaceConnectorLocator.Find(root, connector) is { } found
            ? new LocalConnector(found, Path.GetRelativePath(root, found.DefinitionPath).Replace('\\', '/'))
            : null;

    /// <summary>The workspace around the current directory; null outside one.</summary>
    private static string? FindWorkspace()
        => WorkspaceConnectorLocator.FindWorkspaceRoot(
            TxcServices.GetOptional<IEnvironmentReader>()?.GetCurrentDirectory()
            ?? Directory.GetCurrentDirectory());

    /// <summary>The same keyword match the environment path applies (operation id or summary).</summary>
    private static ConnectorDetail Filter(ConnectorDetail connector, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return connector;

        var matched = connector.Operations
            .Where(o => o.OperationId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (o.Summary?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        return connector with { Operations = matched, MatchedOperations = matched.Count };
    }

    private static DateTimeOffset Now() => (TxcServices.GetOptional<TimeProvider>() ?? TimeProvider.System).GetUtcNow();

    private static Task<string> ReadDefinitionAsync(LocalConnector local, CancellationToken ct)
        => File.ReadAllTextAsync(local.Connector.DefinitionPath, ct);

    internal sealed record LocalConnector(WorkspaceConnector Connector, string RelativeDefinitionPath);
}
