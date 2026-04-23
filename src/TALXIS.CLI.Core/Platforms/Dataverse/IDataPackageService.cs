namespace TALXIS.CLI.Core.Platforms.Dataverse;

/// <summary>
/// Outcome returned by <see cref="IDataPackageService.ImportAsync"/>.
/// </summary>
public sealed record DataPackageImportResult(
    bool Succeeded,
    string? ErrorMessage,
    bool InteractiveAuthRequired);

/// <summary>
/// Imports Configuration-Migration-Tool (CMT) data packages into the Dataverse
/// environment referenced by a profile. Hides subprocess plumbing and MSAL
/// exceptions from feature commands.
/// </summary>
public interface IDataPackageService
{
    Task<DataPackageImportResult> ImportAsync(
        string? profileName,
        string dataPackagePath,
        int connectionCount,
        bool verbose,
        CancellationToken ct);
}
