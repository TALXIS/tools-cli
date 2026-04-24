namespace TALXIS.CLI.Core.Contracts.Packaging;

public sealed record NuGetPackageInstallOptions(
    string PackageName,
    string PackageVersion,
    string? OutputDirectory);
