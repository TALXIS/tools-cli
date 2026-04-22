namespace TALXIS.CLI.Environment.Platforms.Dataverse;

public sealed record NuGetPackageInstallOptions(
    string PackageName,
    string PackageVersion,
    string? OutputDirectory);
