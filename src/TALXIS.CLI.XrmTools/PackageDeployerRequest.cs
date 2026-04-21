namespace TALXIS.CLI.XrmTools;

public sealed record PackageDeployerRequest(
    string PackagePath,
    string? ConnectionString,
    string? EnvironmentUrl,
    bool DeviceCode,
    string? Settings,
    string? LogFile,
    bool LogConsole,
    bool Verbose,
    string? TemporaryArtifactsDirectory,
    int ParentProcessId,
    /// <summary>
    /// NuGet package name used to acquire the deployable package.
    /// When set, Package Deployer is told to record this as <c>packagehistory.uniquename</c>
    /// so that <c>txc deploy show --package-name</c> can look up the run by name.
    /// </summary>
    string? NuGetPackageName = null,
    /// <summary>Resolved NuGet package version, stored in <c>packagehistory</c> alongside <see cref="NuGetPackageName"/>.</summary>
    string? NuGetPackageVersion = null);
