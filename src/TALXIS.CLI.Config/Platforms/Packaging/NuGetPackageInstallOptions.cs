namespace TALXIS.CLI.Config.Platforms.Packaging;

public sealed record NuGetPackageInstallOptions(
    string PackageName,
    string PackageVersion,
    string? OutputDirectory);
