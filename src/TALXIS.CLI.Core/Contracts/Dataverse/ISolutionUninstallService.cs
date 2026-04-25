namespace TALXIS.CLI.Core.Contracts.Dataverse;

public enum SolutionUninstallStatus
{
    Success = 0,
    NotFound = 1,
    Ambiguous = 2,
    Failed = 3,
}

public sealed record SolutionUninstallOutcome(
    string SolutionName,
    Guid? SolutionId,
    SolutionUninstallStatus Status,
    string Message);

/// <summary>
/// Uninstalls a Dataverse solution by unique name. Abstracts profile
/// resolution and connection lifetime so feature commands remain thin.
/// </summary>
public interface ISolutionUninstallService
{
    Task<SolutionUninstallOutcome> UninstallByUniqueNameAsync(
        string? profileName,
        string uniqueName,
        CancellationToken ct);
}
