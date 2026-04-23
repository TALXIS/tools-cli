namespace TALXIS.CLI.Core.Platforms.Packaging;

public sealed record NuGetPackageInstallResult(
    string PackageName,
    string ResolvedVersion,
    string WorkingDirectory,
    string DownloadedPackagePath,
    string ExtractedPackageDirectory,
    string DeployablePackagePath,
    bool UsesTemporaryWorkingDirectory);
