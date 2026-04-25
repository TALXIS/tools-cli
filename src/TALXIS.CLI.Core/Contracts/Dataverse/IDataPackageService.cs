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
}
