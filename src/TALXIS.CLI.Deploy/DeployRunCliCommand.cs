using System.ComponentModel;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.XrmTools;

namespace TALXIS.CLI.Deploy;

[CliCommand(
    Name = "run",
    Description = "Run a deployment. Use --type package or --type solution with --source."
)]
public class DeployRunCliCommand
{
    private readonly NuGetPackageInstallerService _packageInstaller = new();
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeployRunCliCommand));

    [CliOption(Name = "--type", Description = "Deployment type: package or solution.", Required = true)]
    public required string Type { get; set; }

    [CliOption(Name = "--source", Description = "NuGet/package path for package runs, or solution .zip path for solution runs.", Required = true)]
    public required string Source { get; set; }

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL for interactive sign-in.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use device-code flow instead of browser interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--verbose", Description = "Enable verbose logging.", Required = false)]
    public bool Verbose { get; set; }

    // Package options
    [CliOption(Name = "--version", Description = "NuGet package version. Only for --type package.", Required = false)]
    [DefaultValue("latest")]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Download/extract output directory. Only for --type package.", Required = false)]
    public string? OutputDirectory { get; set; }

    [CliOption(Name = "--download-only", Description = "Download/extract without running Package Deployer. Only for --type package.", Required = false)]
    public bool DownloadOnly { get; set; }

    [CliOption(Name = "--settings", Description = "Runtime settings string for Package Deployer. Only for --type package.", Required = false)]
    public string? Settings { get; set; }

    [CliOption(Name = "--log-file", Description = "Path to Package Deployer log file. Only for --type package.", Required = false)]
    public string? LogFile { get; set; }

    [CliOption(Name = "--log-console", Description = "Enable Package Deployer console logging. Only for --type package.", Required = false)]
    public bool LogConsole { get; set; }

    // Solution options
    [CliOption(Name = "--stage-and-upgrade", Description = "Use single-step upgrade when applicable. Only for --type solution.", Required = false)]
    [DefaultValue(true)]
    public bool StageAndUpgrade { get; set; } = true;

    [CliOption(Name = "--force-overwrite", Description = "Overwrite unmanaged customizations. Only for --type solution.", Required = false)]
    public bool ForceOverwrite { get; set; }

    [CliOption(Name = "--publish-workflows", Description = "Activate workflows after import. Only for --type solution.", Required = false)]
    public bool PublishWorkflows { get; set; }

    [CliOption(Name = "--skip-dependency-check", Description = "Skip product-update dependency checks. Only for --type solution.", Required = false)]
    public bool SkipDependencyCheck { get; set; }

    [CliOption(Name = "--skip-lower-version", Description = "Skip import when source version is not higher than target. Only for --type solution.", Required = false)]
    public bool SkipLowerVersion { get; set; }

    [CliOption(Name = "--wait", Description = "Wait for completion (solution runs). By default solution imports return after queueing.", Required = false)]
    public bool Wait { get; set; }

    [CliOption(Name = "--json", Description = "Emit JSON result (currently supported for --type solution).", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            _logger.LogError("--type is required (package|solution).");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(Source))
        {
            _logger.LogError("--source is required.");
            return 1;
        }

        var normalizedType = Type.Trim().ToLowerInvariant();
        return normalizedType switch
        {
            "package" => await RunPackageAsync().ConfigureAwait(false),
            "solution" => await RunSolutionAsync().ConfigureAwait(false),
            _ => InvalidType(),
        };
    }

    private async Task<int> RunPackageAsync()
    {
        if (!StageAndUpgrade || ForceOverwrite || PublishWorkflows || SkipDependencyCheck || SkipLowerVersion || Wait || Json)
        {
            _logger.LogError("Solution-only options were provided with --type package. Remove solution flags (--stage-and-upgrade/--force-overwrite/--publish-workflows/--skip-dependency-check/--skip-lower-version/--wait/--json).");
            return 1;
        }

        // Determine whether the argument is a local file or a NuGet package name.
        bool isLocalFile = File.Exists(Source)
            || Source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || Source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        string packagePath;
        string? tempWorkingDirectory = null;
        string? nugetPackageName = null;
        string? nugetPackageVersion = null;

        if (isLocalFile)
        {
            if (!File.Exists(Source))
            {
                _logger.LogError("Package file not found: {PackagePath}", Source);
                return 1;
            }

            packagePath = Path.GetFullPath(Source);
            _logger.LogInformation("Using local package: {PackagePath}", packagePath);
        }
        else
        {
            NuGetPackageInstallOptions options = new(
                Source,
                PackageVersion,
                OutputDirectory);

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

        string? resolvedConnectionString = ServiceClientFactory.ResolveConnectionString(ConnectionString);
        string? resolvedEnvironmentUrl = ServiceClientFactory.ResolveEnvironmentUrl(EnvironmentUrl);
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

    private async Task<int> RunSolutionAsync()
    {
        if (DownloadOnly || !string.IsNullOrWhiteSpace(OutputDirectory) || !string.IsNullOrWhiteSpace(Settings) || !string.IsNullOrWhiteSpace(LogFile) || LogConsole)
        {
            _logger.LogError("Package-only options were provided with --type solution. Remove package flags (--download-only/--output/--settings/--log-file/--log-console).");
            return 1;
        }

        string solutionPath = Path.GetFullPath(Source);
        if (!File.Exists(solutionPath))
        {
            _logger.LogError("Solution file not found: {Path}", solutionPath);
            return 1;
        }

        string? resolvedConnectionString = ServiceClientFactory.ResolveConnectionString(ConnectionString);
        string? resolvedEnvironmentUrl = ServiceClientFactory.ResolveEnvironmentUrl(EnvironmentUrl);

        if (string.IsNullOrWhiteSpace(resolvedConnectionString) && string.IsNullOrWhiteSpace(resolvedEnvironmentUrl))
        {
            _logger.LogError("Dataverse authentication is required. Pass --connection-string, pass --environment for interactive sign-in, or set DATAVERSE_CONNECTION_STRING / TXC_DATAVERSE_CONNECTION_STRING / DATAVERSE_ENVIRONMENT_URL / TXC_DATAVERSE_ENVIRONMENT_URL.");
            return 1;
        }

        SolutionInfo source;
        try
        {
            source = SolutionImporter.ReadSolutionInfo(solutionPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            _logger.LogError(ex, "Unable to read solution metadata from {Path}", solutionPath);
            return 1;
        }

        _logger.LogInformation("Source solution: {UniqueName} {Version} ({Managed})",
            source.UniqueName, source.Version, source.Managed ? "managed" : "unmanaged");

        DataverseConnection conn;
        try
        {
            conn = ServiceClientFactory.Connect(ConnectionString, EnvironmentUrl, DeviceCode, Verbose, _logger);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }

        using (conn)
        {
            var client = conn.Client;
            try
            {
                var importer = new SolutionImporter(client, _logger);
                var existing = await importer.GetExistingSolutionAsync(source.UniqueName).ConfigureAwait(false);
                var plannedPath = SolutionImporter.SelectImportPath(source, existing, StageAndUpgrade);
                bool smartDiffExpected = SolutionImporter.SmartDiffExpected(plannedPath, ForceOverwrite);

                _logger.LogInformation("Planned import path: {Path}", FormatPath(plannedPath));
                _logger.LogInformation("SmartDiff expected: {SmartDiff}", smartDiffExpected ? "yes" : "no");

                EmitWarnings(plannedPath, ForceOverwrite);

                var options = new SolutionImportOptions(
                    StageAndUpgrade: StageAndUpgrade,
                    ForceOverwrite: ForceOverwrite,
                    PublishWorkflows: PublishWorkflows,
                    SkipDependencyCheck: SkipDependencyCheck,
                    SkipLowerVersion: SkipLowerVersion,
                    Async: !Wait);

                var result = await importer.ImportAsync(solutionPath, options).ConfigureAwait(false);

                if (Json)
                {
                    var payload = new
                    {
                        path = FormatPath(result.Path),
                        uniqueName = result.Source.UniqueName,
                        sourceVersion = result.Source.Version.ToString(),
                        sourceManaged = result.Source.Managed,
                        existingVersion = result.ExistingTarget?.Version.ToString(),
                        existingManaged = result.ExistingTarget?.Managed,
                        importJobId = result.ImportJobId,
                        asyncOperationId = result.AsyncOperationId,
                        startedAtUtc = result.StartedAtUtc.ToString("O"),
                        completedAtUtc = result.CompletedAtUtc?.ToString("O"),
                        smartDiffExpected = result.SmartDiffExpected,
                        status = result.Status,
                    };
                    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                }

                _logger.LogInformation("Import path: {Path}", FormatPath(result.Path));
                _logger.LogInformation("Status: {Status}", result.Status);
                _logger.LogInformation("ImportJobId: {ImportJobId}", result.ImportJobId);
                if (result.AsyncOperationId is { } asyncId)
                {
                    _logger.LogInformation("AsyncOperationId: {AsyncOperationId}", asyncId);
                }
                _logger.LogInformation("Started (UTC): {Start}", result.StartedAtUtc.ToString("O"));
                if (result.CompletedAtUtc is { } completed)
                {
                    _logger.LogInformation("Completed (UTC): {End}", completed.ToString("O"));
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Solution import failed");
                return 1;
            }
        }
    }

    private void EmitWarnings(SolutionImportPath plannedPath, bool forceOverwrite)
    {
        if (forceOverwrite && plannedPath == SolutionImportPath.Upgrade)
        {
            _logger.LogWarning("--force-overwrite disables SmartDiff; expect a full re-import.");
        }

        if (plannedPath == SolutionImportPath.Update)
        {
            _logger.LogWarning("Plain update does not delete components removed from the source solution.");
        }
    }

    private static string FormatPath(SolutionImportPath path) => path switch
    {
        SolutionImportPath.Install => "install",
        SolutionImportPath.Update => "update",
        SolutionImportPath.Upgrade => "single-step upgrade",
        _ => path.ToString()
    };

    private int InvalidType()
    {
        _logger.LogError("Invalid --type '{Type}'. Expected 'package' or 'solution'.", Type);
        return 1;
    }
}
