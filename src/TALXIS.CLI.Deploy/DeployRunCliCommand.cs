using System.ComponentModel;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Deploy;

[CliCommand(
    Name = "run",
    Description = "Run a deployment. Use --type package or --type solution with --source."
)]
public class DeployRunCliCommand
{
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

    [CliOption(Name = "--deployable-package", Description = "Deployable package file name inside a NuGet package. Only for --type package.", Required = false)]
    public string? DeployablePackageName { get; set; }

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

        var cmd = new DeployPackageCliCommand
        {
            Package = Source,
            PackageVersion = PackageVersion,
            DeployablePackageName = DeployablePackageName,
            OutputDirectory = OutputDirectory,
            DownloadOnly = DownloadOnly,
            ConnectionString = ConnectionString,
            EnvironmentUrl = EnvironmentUrl,
            DeviceCode = DeviceCode,
            Settings = Settings,
            LogFile = LogFile,
            LogConsole = LogConsole,
            Verbose = Verbose,
        };
        return await cmd.RunAsync().ConfigureAwait(false);
    }

    private async Task<int> RunSolutionAsync()
    {
        if (DownloadOnly || !string.IsNullOrWhiteSpace(DeployablePackageName) || !string.IsNullOrWhiteSpace(OutputDirectory) || !string.IsNullOrWhiteSpace(Settings) || !string.IsNullOrWhiteSpace(LogFile) || LogConsole)
        {
            _logger.LogError("Package-only options were provided with --type solution. Remove package flags (--download-only/--deployable-package/--output/--settings/--log-file/--log-console).");
            return 1;
        }

        var cmd = new DeploySolutionCliCommand
        {
            SolutionZip = Source,
            ConnectionString = ConnectionString,
            EnvironmentUrl = EnvironmentUrl,
            DeviceCode = DeviceCode,
            StageAndUpgrade = StageAndUpgrade,
            ForceOverwrite = ForceOverwrite,
            PublishWorkflows = PublishWorkflows,
            SkipDependencyCheck = SkipDependencyCheck,
            SkipLowerVersion = SkipLowerVersion,
            Wait = Wait,
            Json = Json,
            Verbose = Verbose,
        };
        return await cmd.RunAsync().ConfigureAwait(false);
    }

    private int InvalidType()
    {
        _logger.LogError("Invalid --type '{Type}'. Expected 'package' or 'solution'.", Type);
        return 1;
    }
}
