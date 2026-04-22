using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Abstractions;

/// <summary>
/// Walks the filesystem upward from <c>startDirectory</c> looking for
/// <c>.txc/workspace.json</c>. First hit wins; returns null if none found.
/// </summary>
public interface IWorkspaceDiscovery
{
    Task<WorkspaceResolution?> DiscoverAsync(string startDirectory, CancellationToken ct);
}

public sealed record WorkspaceResolution(string WorkspaceRoot, string WorkspaceFilePath, WorkspaceConfig Config);
