using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;
using TALXIS.CLI.XrmTools;

namespace TALXIS.CLI.Deploy;

public class DeployPackageCliCommand
{
    private readonly NuGetPackageInstallerService _packageInstaller = new();
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeployPackageCliCommand));

    [CliArgument(Description = "NuGet package name, or path to a local .pdpkg.zip / package DLL")]
    public required string Package { get; set; }

    [CliOption(Name = "--version", Description = "NuGet package version to install. Defaults to 'latest'. Only used with NuGet packages.", Required = false)]
    [DefaultValue("latest")]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--deployable-package", Description = "Deployable package file name inside the NuGet package. Use when the package contains multiple .pdpkg files.", Required = false)]
    public string? DeployablePackageName { get; set; }

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Directory where the downloaded and extracted files should be written. Defaults to a temporary directory.", Required = false)]
    public string? OutputDirectory { get; set; }

    [CliOption(Name = "--download-only", Description = "Download and extract the deployable package without invoking Package Deployer. Only used with NuGet packages.", Required = false)]
    public bool DownloadOnly { get; set; }

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string used by the managed Package Deployer host. If omitted, txc checks DATAVERSE_CONNECTION_STRING and TXC_DATAVERSE_CONNECTION_STRING.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL used for PAC-style interactive sign-in when no connection string is provided.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use Microsoft Entra device code flow instead of opening a browser for interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--settings", Description = "Runtime settings string passed to Package Deployer.", Required = false)]
    public string? Settings { get; set; }

    [CliOption(Name = "--log-file", Description = "Optional Package Deployer log file path.", Required = false)]
    public string? LogFile { get; set; }

    [CliOption(Name = "--log-console", Description = "Enable Package Deployer console logging.", Required = false)]
    public bool LogConsole { get; set; }

    [CliOption(Name = "--verbose", Description = "Enable verbose Package Deployer logging.", Required = false)]
    public bool Verbose { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Package))
        {
            _logger.LogError("A NuGet package name or local package path must be provided.");
            return 1;
        }

        // Determine whether the argument is a local file or a NuGet package name.
        bool isLocalFile = File.Exists(Package)
            || Package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || Package.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        string packagePath;
        string? tempWorkingDirectory = null;
        string? nugetPackageName = null;
        string? nugetPackageVersion = null;

        if (isLocalFile)
        {
            if (!File.Exists(Package))
            {
                _logger.LogError("Package file not found: {PackagePath}", Package);
                return 1;
            }

            packagePath = Path.GetFullPath(Package);
            _logger.LogInformation("Using local package: {PackagePath}", packagePath);
        }
        else
        {
            // NuGet download flow
            EnvironmentInstallOptions options = new(
                Package,
                PackageVersion,
                DeployablePackageName,
                OutputDirectory,
                DownloadOnly,
                ResolveConnectionString(ConnectionString),
                ResolveEnvironmentUrl(EnvironmentUrl),
                DeviceCode,
                Settings,
                LogFile,
                LogConsole,
                Verbose);

            NuGetPackageInstallResult installResult = await _packageInstaller.InstallAsync(options);

            _logger.LogInformation("Resolved {PackageName} version {Version}", installResult.PackageName, installResult.ResolvedVersion);
            _logger.LogInformation("Deployable package extracted to {Path}", installResult.DeployablePackagePath);

            nugetPackageName = installResult.PackageName;
            nugetPackageVersion = installResult.ResolvedVersion;

            if (DownloadOnly)
            {
                return 0;
            }

            packagePath = installResult.DeployablePackagePath;

            if (installResult.UsesTemporaryWorkingDirectory)
            {
                tempWorkingDirectory = installResult.WorkingDirectory;
            }
        }

        string? resolvedConnectionString = ResolveConnectionString(ConnectionString);
        string? resolvedEnvironmentUrl = ResolveEnvironmentUrl(EnvironmentUrl);
        PackageDeployerResult? deployResult = null;
        string packageDeployerArtifactsDirectory = Path.Combine(
            Path.GetTempPath(),
            "txc",
            "package-deployer-host",
            Guid.NewGuid().ToString("N"));

        if (string.IsNullOrWhiteSpace(resolvedConnectionString) && string.IsNullOrWhiteSpace(resolvedEnvironmentUrl))
        {
            _logger.LogError("Dataverse authentication is required to run Package Deployer. Pass --connection-string, pass --environment for interactive sign-in, or set DATAVERSE_CONNECTION_STRING / TXC_DATAVERSE_CONNECTION_STRING / DATAVERSE_ENVIRONMENT_URL / TXC_DATAVERSE_ENVIRONMENT_URL.");
            _logger.LogError("Package located at {PackagePath}", packagePath);
            return 1;
        }

        try
        {
            deployResult = await PackageDeployerSubprocess.RunAsync(new PackageDeployerRequest(
                packagePath,
                resolvedConnectionString,
                resolvedEnvironmentUrl,
                DeviceCode,
                Settings,
                LogFile,
                LogConsole,
                Verbose,
                packageDeployerArtifactsDirectory,
                System.Environment.ProcessId,
                NuGetPackageName: nugetPackageName,
                NuGetPackageVersion: nugetPackageVersion));

            if (!deployResult.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(deployResult.ErrorMessage))
                {
                    _logger.LogError("{ErrorMessage}", deployResult.ErrorMessage);
                }

                if (!string.IsNullOrWhiteSpace(LogFile) && !string.IsNullOrWhiteSpace(deployResult.LogFilePath))
                {
                    _logger.LogError("Detailed Package Deployer log: {LogPath}", deployResult.LogFilePath);
                }

                if (!string.IsNullOrWhiteSpace(LogFile) && !string.IsNullOrWhiteSpace(deployResult.CmtLogFilePath))
                {
                    _logger.LogError("Detailed CMT import log: {LogPath}", deployResult.CmtLogFilePath);
                }
                else if (string.IsNullOrWhiteSpace(LogFile) &&
                    (!string.IsNullOrWhiteSpace(deployResult.LogFilePath) || !string.IsNullOrWhiteSpace(deployResult.CmtLogFilePath)))
                {
                    _logger.LogWarning("Detailed temporary logs were cleaned up. Pass --log-file to preserve them.");
                }

                _logger.LogError("Package deploy failed. Package located at {PackagePath}", packagePath);
                return 1;
            }

            _logger.LogInformation("Package deploy completed successfully.");

            if (!string.IsNullOrWhiteSpace(LogFile))
            {
                _logger.LogInformation("Package Deployer log: {LogPath}", Path.GetFullPath(LogFile));
            }

            return 0;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Package deploy failed");
            _logger.LogError("Package located at {PackagePath}", packagePath);
            return 1;
        }
        finally
        {
            PackageDeployerSubprocess.TryDeleteDirectory(packageDeployerArtifactsDirectory);

            if (!string.IsNullOrWhiteSpace(tempWorkingDirectory))
            {
                PackageDeployerSubprocess.TryDeleteDirectory(tempWorkingDirectory);
            }
        }
    }

    private static string? ResolveConnectionString(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return System.Environment.GetEnvironmentVariable("DATAVERSE_CONNECTION_STRING")
            ?? System.Environment.GetEnvironmentVariable("TXC_DATAVERSE_CONNECTION_STRING");
    }

    private static string? ResolveEnvironmentUrl(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        return System.Environment.GetEnvironmentVariable("DATAVERSE_ENVIRONMENT_URL")
            ?? System.Environment.GetEnvironmentVariable("TXC_DATAVERSE_ENVIRONMENT_URL");
    }
}
