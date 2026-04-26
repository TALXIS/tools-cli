namespace TALXIS.CLI.Core.Contracts.Dataverse;

public enum SolutionUninstallStatus
{
    Success = 0,
    NotFound = 1,
    Ambiguous = 2,
    Failed = 3,
    /// <summary>
    /// The solution type (managed/unmanaged) does not match what the caller expected.
    /// For example, trying to uninstall an unmanaged solution or delete a managed one.
    /// </summary>
    TypeMismatch = 4,
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
    /// <summary>
    /// Deletes a solution by unique name. When <paramref name="expectManaged"/>
    /// is specified, rejects solutions whose type doesn't match.
    /// </summary>
    /// <param name="expectManaged">
    /// <c>true</c> = managed only (uninstall), <c>false</c> = unmanaged only (delete),
    /// <c>null</c> = no type check (legacy).
    /// </param>
    Task<SolutionUninstallOutcome> UninstallByUniqueNameAsync(
        string? profileName,
        string uniqueName,
        bool? expectManaged = null,
        CancellationToken ct = default);
}
