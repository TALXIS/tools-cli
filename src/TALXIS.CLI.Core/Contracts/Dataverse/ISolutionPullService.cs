namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record SolutionPullOptions(
    string SolutionUniqueName,
    string SolutionRootPath,
    string? ProjectFilePath);

public sealed record SolutionPullResult(
    string SolutionRootPath,
    IReadOnlyList<string> NormalizedAssemblies,
    IReadOnlyList<string> ExcludedBinaries,
    IReadOnlyList<string> ExcludedWebResources,
    IReadOnlyList<string> ExcludedPcfControls,
    IReadOnlyList<string> RemovedFiles);

public interface ISolutionPullService
{
    Task<SolutionPullResult> PullAsync(string? profileName, SolutionPullOptions options, CancellationToken ct);
}
