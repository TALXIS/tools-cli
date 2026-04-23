namespace TALXIS.CLI.Config.Providers.Dataverse.Platforms;

public sealed record NuGetPackageInstallOptions(
    string PackageName,
    string PackageVersion,
    string? OutputDirectory);
