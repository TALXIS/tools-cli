using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// Connector metadata from the most local source that has it: a custom
/// connector defined in the workspace is read from its own definition file,
/// with no profile and no environment call; anything else comes from the
/// connected environment.
/// </summary>
internal static class ConnectorMetadataLookup
{
    public static async Task<ConnectorDetail> GetConnectorAsync(string? profile, string connector, string? search, CancellationToken ct)
    {
        if (FindWorkspaceConnector(connector) is { } local)
        {
            var detail = TxcServices.Get<IConnectorDefinitionReader>()
                .ReadConnector(await ReadDefinitionAsync(local, ct).ConfigureAwait(false), local.Connector.Identifier, search);
            return detail with { Source = ConnectorMetadataSource.Workspace, SourcePath = local.RelativeDefinitionPath };
        }

        var remote = await TxcServices.Get<IConnectorMetadataService>().GetConnectorAsync(profile, connector, search, ct).ConfigureAwait(false);
        return remote with { Source = ConnectorMetadataSource.Environment };
    }

    public static async Task<OperationDetail> GetOperationAsync(string? profile, string connector, string operation, CancellationToken ct)
    {
        if (FindWorkspaceConnector(connector) is { } local)
        {
            var detail = TxcServices.Get<IConnectorDefinitionReader>()
                .ReadOperation(await ReadDefinitionAsync(local, ct).ConfigureAwait(false), local.Connector.Identifier, operation);
            return detail with { Source = ConnectorMetadataSource.Workspace, SourcePath = local.RelativeDefinitionPath };
        }

        return await GetFromEnvironmentAsync(profile, connector, operation, ct).ConfigureAwait(false);
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

    /// <summary>The workspace connector <paramref name="connector"/> names, searched from the current directory.</summary>
    internal static LocalConnector? FindWorkspaceConnector(string connector, string? startDirectory = null)
    {
        var current = startDirectory
            ?? TxcServices.GetOptional<IEnvironmentReader>()?.GetCurrentDirectory()
            ?? Directory.GetCurrentDirectory();
        var root = WorkspaceConnectorLocator.FindWorkspaceRoot(current);
        return root is not null && WorkspaceConnectorLocator.Find(root, connector) is { } found
            ? new LocalConnector(found, Path.GetRelativePath(root, found.DefinitionPath).Replace('\\', '/'))
            : null;
    }

    private static Task<string> ReadDefinitionAsync(LocalConnector local, CancellationToken ct)
        => File.ReadAllTextAsync(local.Connector.DefinitionPath, ct);

    internal sealed record LocalConnector(WorkspaceConnector Connector, string RelativeDefinitionPath);
}
