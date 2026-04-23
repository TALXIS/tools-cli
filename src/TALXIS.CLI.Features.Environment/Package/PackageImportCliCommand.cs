using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Platforms.Dataverse;
using TALXIS.CLI.Config.Platforms.Packaging;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Package;

[CliCommand(
    Name = "import",
    Description = "Import a deployable package into the target environment."
)]
public class PackageImportCliCommand : ProfiledCliCommand
{
    private readonly NuGetPackageInstallerService _packageInstaller = new();
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(PackageImportCliCommand));

    [CliArgument(Name = "package", Description = "NuGet package name, local .pdpkg.zip/.pdpkg/.zip archive path, or extracted package folder path.", Required = true)]
    public required string Package { get; set; }

    [CliOption(Name = "--version", Description = "NuGet package version (only when 'package' is a NuGet name).", Required = false)]
    [DefaultValue("latest")]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Download/extract output directory.", Required = false)]
    public string? OutputDirectory { get; set; }

    [CliOption(Name = "--download-only", Description = "Download/extract without running Package Deployer.", Required = false)]
    public bool DownloadOnly { get; set; }

    [CliOption(Name = "--settings", Description = "Runtime settings string for Package Deployer.", Required = false)]
    public string? Settings { get; set; }

    [CliOption(Name = "--log-file", Description = "Path to Package Deployer log file.", Required = false)]
    public string? LogFile { get; set; }

    [CliOption(Name = "--log-console", Description = "Enable Package Deployer console logging.", Required = false)]
    public bool LogConsole { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Package))
        {
            _logger.LogError("'package' argument is required.");
            return 1;
        }

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
            var options = new NuGetPackageInstallOptions(Package, PackageVersion, OutputDirectory);
            var installResult = await _packageInstaller.InstallAsync(options);

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

        PackageImportResult result;
        try
        {
            var service = TxcServices.Get<IPackageImportService>();
            result = await service.ImportAsync(new PackageImportRequest(
                ProfileName: Profile,
                PackagePath: packagePath,
                Settings: Settings,
                LogFile: LogFile,
                LogConsole: LogConsole,
                Verbose: Verbose,
                NuGetPackageName: nugetPackageName,
                NuGetPackageVersion: nugetPackageVersion,
                TempWorkingDirectory: tempWorkingDirectory), CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Package import failed");
            _logger.LogError("Package located at {PackagePath}", packagePath);
            return 1;
        }

        if (result.InteractiveAuthRequired)
        {
            _logger.LogError("Interactive authentication is required. Run 'txc config auth login' for profile '{Profile}' and retry.", Profile ?? "(default)");
            return 1;
        }

        if (!result.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                _logger.LogError("{ErrorMessage}", result.ErrorMessage);
            }

            if (!string.IsNullOrWhiteSpace(LogFile) && !string.IsNullOrWhiteSpace(result.LogFilePath))
            {
                _logger.LogError("Detailed Package Deployer log: {LogPath}", result.LogFilePath);
            }

            if (!string.IsNullOrWhiteSpace(LogFile) && !string.IsNullOrWhiteSpace(result.CmtLogFilePath))
            {
                _logger.LogError("Detailed CMT import log: {LogPath}", result.CmtLogFilePath);
            }
            else if (string.IsNullOrWhiteSpace(LogFile) &&
                (!string.IsNullOrWhiteSpace(result.LogFilePath) || !string.IsNullOrWhiteSpace(result.CmtLogFilePath)))
            {
                _logger.LogWarning("Detailed temporary logs were cleaned up. Pass --log-file to preserve them.");
            }

            _logger.LogError("Package import failed. Package located at {PackagePath}", packagePath);
            return 1;
        }

        _logger.LogInformation("Package import completed successfully.");
        if (!string.IsNullOrWhiteSpace(LogFile))
        {
            _logger.LogInformation("Package Deployer log: {LogPath}", Path.GetFullPath(LogFile));
        }
        return 0;
    }
}
