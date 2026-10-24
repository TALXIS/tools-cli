namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Outcome returned by <see cref="IDataPackageService.ImportAsync"/>.
/// </summary>
public sealed record DataPackageImportResult(
    bool Succeeded,
    string? ErrorMessage,
    bool InteractiveAuthRequired);

/// <summary>
/// Outcome returned by <see cref="IDataPackageService.ExportAsync"/>.
/// </summary>
public sealed record DataPackageExportResult(
    bool Succeeded,
    string? ErrorMessage,
    bool InteractiveAuthRequired);

/// <summary>
/// What to do when a record listed in the package cannot be deleted by its
/// primary-key GUID (server returns <c>ObjectDoesNotExist</c>).
/// </summary>
public enum DataPackageCleanupMissingAction
{
    /// <summary>Look the record up by the schema's natural-key columns and delete that match if found.</summary>
    ByNaturalKey = 0,
    /// <summary>Count the record as not-found and move on.</summary>
    Skip = 1,
    /// <summary>Abort the run on the first miss.</summary>
    Fail = 2,
}

/// <summary>
/// Per-call tuning options for <see cref="IDataPackageService.CleanupAsync"/>.
/// </summary>
public sealed record DataPackageCleanupOptions(
    int BatchSize,
    int ConnectionCount,
    bool DryRun,
    DataPackageCleanupMissingAction MissingAction,
    bool ContinueOnError);

/// <summary>
/// Per-entity cleanup statistics returned by <see cref="IDataPackageService.CleanupAsync"/>.
/// </summary>
public sealed record DataPackageCleanupEntityResult(
    string EntityLogicalName,
    int Total,
    int DeletedByGuid,
    int DeletedByNaturalKey,
    int NotFound,
    int Errors,
    IReadOnlyList<string> ErrorMessages);

/// <summary>
/// Outcome returned by <see cref="IDataPackageService.CleanupAsync"/>.
/// </summary>
public sealed record DataPackageCleanupResult(
    bool Succeeded,
    string? ErrorMessage,
    bool InteractiveAuthRequired,
    IReadOnlyList<DataPackageCleanupEntityResult> EntityResults,
    int TotalDeletedByGuid,
    int TotalDeletedByNaturalKey,
    int TotalNotFound,
    int TotalErrors,
    int M2mDisassociations);

/// <summary>
/// Imports and exports Configuration-Migration-Tool (CMT) data packages
/// for the Dataverse environment referenced by a profile. Hides subprocess
/// plumbing and MSAL exceptions from feature commands.
/// </summary>
public interface IDataPackageService
{
    Task<DataPackageImportResult> ImportAsync(
        string? profileName,
        string dataPackagePath,
        int connectionCount,
        bool batchMode,
        int batchSize,
        bool overrideSafetyChecks,
        int prefetchLimit,
        bool verbose,
        CancellationToken ct);

    Task<DataPackageExportResult> ExportAsync(
        string? profileName,
        string schemaPath,
        string outputPath,
        bool exportFiles,
        bool verbose,
        CancellationToken ct);

    /// <summary>
    /// Deletes every record described by a CMT data package from the live
    /// environment referenced by <paramref name="profileName"/>. Entities are
    /// processed in reverse <c>&lt;entityImportOrder&gt;</c>. Deletes first
    /// dispatch by the record's GUID (<c>&lt;record id&gt;</c>); on
    /// <c>ObjectDoesNotExist</c> the schema's primary-name field and every
    /// <c>updateCompare="true"</c> field are used as a natural-key fallback
    /// per <paramref name="options"/>.
    /// </summary>
    Task<DataPackageCleanupResult> CleanupAsync(
        string? profileName,
        string dataPackagePath,
        DataPackageCleanupOptions options,
        bool verbose,
        CancellationToken ct);
}
