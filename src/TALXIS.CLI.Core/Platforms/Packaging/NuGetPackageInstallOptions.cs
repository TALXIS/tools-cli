namespace TALXIS.CLI.Core.Platforms.Packaging;

public sealed record NuGetPackageInstallOptions(
    string PackageName,
    string PackageVersion,
    string? OutputDirectory);
