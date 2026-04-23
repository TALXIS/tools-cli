namespace TALXIS.CLI.Config.Platforms.Dataverse;

/// <summary>
/// Input for <see cref="IPackageImportService.ImportAsync"/>.
/// </summary>
public sealed record PackageImportRequest(
    string? ProfileName,
    string PackagePath,
    string? Settings,
    string? LogFile,
    bool LogConsole,
    bool Verbose,
    string? NuGetPackageName,
    string? NuGetPackageVersion,
    /// <summary>
    /// Temporary working directory created by NuGet restore. Service is responsible for
    /// deleting it after the import attempt, regardless of outcome.
    /// </summary>
    string? TempWorkingDirectory);

/// <summary>
/// Result of a single package import attempt.
/// </summary>
public sealed record PackageImportResult(
    bool Succeeded,
    string? ErrorMessage,
    string? LogFilePath,
    string? CmtLogFilePath,
    /// <summary>
    /// Set when the configured credential requires interactive reauthentication
    /// (equivalent to an MSAL UI-required prompt). The command layer should translate this
    /// into a user-facing hint, without taking a dependency on MSAL types.
    /// </summary>
    bool InteractiveAuthRequired);

/// <summary>
/// Provider-agnostic service for running a deployable-package import into the target environment.
/// </summary>
public interface IPackageImportService
{
    Task<PackageImportResult> ImportAsync(PackageImportRequest request, CancellationToken ct);
}
