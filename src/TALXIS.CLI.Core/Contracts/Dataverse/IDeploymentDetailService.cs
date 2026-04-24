namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Discriminator for <see cref="DeploymentDetailResult"/>.
/// </summary>
public enum DeploymentRunKind
{
    Package,
    Solution,
    AsyncOperationInProgress,
    AsyncOperationCompleted,
}

/// <summary>
/// Status summary for a raw <c>asyncoperation</c> row when no correlated
/// solution-history record is available (yet).
/// </summary>
public sealed record AsyncOperationSummary(
    Guid Id,
    string StateLabel,
    int StateCode,
    int StatusCode,
    bool Completed,
    bool Succeeded,
    string? Message);

/// <summary>
/// Unified result for <c>txc environment deployment show</c>. Sub-fields are populated
/// according to <see cref="Kind"/>; unused slots are null / empty.
/// </summary>
public sealed record DeploymentDetailResult(
    DeploymentRunKind Kind,
    PackageHistoryRecord? Package,
    IReadOnlyList<SolutionHistoryRecord> CorrelatedSolutions,
    SolutionHistoryRecord? Solution,
    PackageHistoryRecord? ParentPackage,
    Guid? ImportJobId,
    string? FormattedImportLog,
    AsyncOperationSummary? AsyncOperation,
    IReadOnlyList<string> Findings);

/// <summary>
/// Provider-agnostic service for resolving a single deployment run (package or solution)
/// for the <c>deployment show</c> command.
/// </summary>
public interface IDeploymentDetailService
{
    Task<DeploymentDetailResult?> GetByPackageRunIdAsync(string? profileName, Guid id, CancellationToken ct);
    Task<DeploymentDetailResult?> GetBySolutionRunIdAsync(string? profileName, Guid id, bool includeFull, CancellationToken ct);
    Task<DeploymentDetailResult?> GetByAsyncOperationIdAsync(string? profileName, Guid id, bool includeFull, CancellationToken ct);
    Task<DeploymentDetailResult?> GetLatestByPackageNameAsync(string? profileName, string packageName, CancellationToken ct);
    Task<DeploymentDetailResult?> GetLatestBySolutionNameAsync(string? profileName, string solutionName, bool includeFull, CancellationToken ct);
    Task<DeploymentDetailResult?> GetLatestAsync(string? profileName, bool includeFull, CancellationToken ct);
}
