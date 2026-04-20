namespace TALXIS.CLI.Deploy;

public sealed record NuGetPackageInstallResult(
    string PackageName,
    string ResolvedVersion,
    string WorkingDirectory,
    string DownloadedPackagePath,
    string ExtractedPackageDirectory,
    string DeployablePackagePath,
    bool UsesTemporaryWorkingDirectory);
