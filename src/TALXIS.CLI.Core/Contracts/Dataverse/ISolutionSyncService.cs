namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record SolutionSyncOptions(
    string SolutionUniqueName,
    string SolutionRootPath,
    string? ProjectFilePath);

public sealed record SolutionSyncResult(
    string SolutionRootPath,
    IReadOnlyList<string> NormalizedAssemblies,
    IReadOnlyList<string> ExcludedBinaries,
    IReadOnlyList<string> RemovedFiles);

public interface ISolutionSyncService
{
    Task<SolutionSyncResult> SyncAsync(string? profileName, SolutionSyncOptions options, CancellationToken ct);
}
