namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Row from the Dataverse <c>packagehistory</c> table (Package Deployer run records).
/// All <see cref="DateTime"/> values are UTC.
/// </summary>
public sealed record PackageHistoryRecord(
    Guid Id,
    string? Name,
    string? Status,
    string? Stage,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    Guid? OperationId,
    string? Message,
    Guid? CorrelationId = null);

/// <summary>
/// Row from the Dataverse <c>msdyn_solutionhistory</c> table. Operation /
/// suboperation codes are already resolved to human-readable labels.
/// </summary>
public sealed record SolutionHistoryRecord(
    Guid Id,
    string? SolutionName,
    string? SolutionVersion,
    string? PackageName,
    int? OperationCode,
    string OperationLabel,
    int? SuboperationCode,
    string SuboperationLabel,
    bool? OverwriteUnmanagedCustomizations,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? Result,
    Guid? ActivityId = null);

public sealed record DeploymentHistorySnapshot(
    IReadOnlyList<PackageHistoryRecord> Packages,
    IReadOnlyList<SolutionHistoryRecord> Solutions);

/// <summary>
/// Reads Package Deployer and solution-import history from the target
/// environment. Hides the two underlying readers and the connection lifetime
/// from feature commands.
/// </summary>
public interface IDeploymentHistoryService
{
    Task<DeploymentHistorySnapshot> GetRecentAsync(
        string? profileName,
        bool includePackages,
        bool includeSolutions,
        int maxCount,
        DateTime? sinceUtc,
        bool problemsOnly,
        CancellationToken ct);
}
