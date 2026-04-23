namespace TALXIS.CLI.XrmTools;

/// <summary>
/// Transport contract between the <c>txc</c> parent process and the
/// <c>__txc_internal_package_deployer</c> subprocess. Intentionally contains
/// NO secrets — the child re-resolves the credential from the OS vault via
/// <c>IConfigurationResolver</c> using <see cref="ProfileId"/> (and, if set,
/// <see cref="ConfigDirectory"/> to scope the vault to a specific
/// <c>TXC_CONFIG_DIR</c>).
/// </summary>
public sealed record PackageDeployerRequest(
    string PackagePath,
    string ProfileId,
    string? ConfigDirectory,
    string? Settings,
    string? LogFile,
    bool LogConsole,
    bool Verbose,
    string? TemporaryArtifactsDirectory,
    int ParentProcessId,
    /// <summary>
    /// NuGet package name used to acquire the deployable package.
    /// When set, Package Deployer is told to record this as <c>packagehistory.uniquename</c>
    /// so that <c>txc environment deployment show --package-name</c> can look up the run by name.
    /// </summary>
    string? NuGetPackageName = null,
    /// <summary>Resolved NuGet package version, stored in <c>packagehistory</c> alongside <see cref="NuGetPackageName"/>.</summary>
    string? NuGetPackageVersion = null);
