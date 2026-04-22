using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Deploy;

[CliCommand(
    Name = "uninstall",
    Description = "Uninstall solutions from the target environment by solution name or by package source."
)]
public class DeployUninstallCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DeployUninstallCliCommand));

    [CliOption(Name = "--solution-name", Description = "Uninstall a single solution by unique name.", Required = false)]
    public string? SolutionName { get; set; }

    [CliOption(Name = "--package-source", Description = "Package source used to derive uninstall order from ImportConfig. Accepts NuGet package name, local .pdpkg.zip/.pdpkg file, or an extracted package folder.", Required = false)]
    public string? PackageSource { get; set; }

    [CliOption(Name = "--version", Description = "NuGet package version for --package-source when source is a NuGet package name. Defaults to 'latest'.", Required = false)]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Directory for temporary/downloaded package assets when resolving --package-source from NuGet.", Required = false)]
    public string? OutputDirectory { get; set; }

    [CliOption(Name = "--force", Aliases = ["--yes"], Description = "Confirm destructive uninstall actions.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--connection-string", Description = "Dataverse connection string. If omitted, txc checks DATAVERSE_CONNECTION_STRING and TXC_DATAVERSE_CONNECTION_STRING.", Required = false)]
    public string? ConnectionString { get; set; }

    [CliOption(Name = "--environment", Description = "Dataverse environment URL for interactive sign-in when no connection string is provided.", Required = false)]
    public string? EnvironmentUrl { get; set; }

    [CliOption(Name = "--device-code", Description = "Use Microsoft Entra device code flow instead of opening a browser for interactive sign-in.", Required = false)]
    public bool DeviceCode { get; set; }

    [CliOption(Name = "--json", Description = "Emit uninstall result as JSON.", Required = false)]
    public bool Json { get; set; }

    [CliOption(Name = "--verbose", Description = "Enable verbose logging.", Required = false)]
    public bool Verbose { get; set; }

    public async Task<int> RunAsync()
    {
        if (!Yes)
        {
            _logger.LogError("Uninstall is destructive. Pass --force to confirm.");
            return 1;
        }

        bool hasSolutionSelector = !string.IsNullOrWhiteSpace(SolutionName);
        bool hasPackageSelector = !string.IsNullOrWhiteSpace(PackageSource);
        int selectors = (hasSolutionSelector ? 1 : 0) + (hasPackageSelector ? 1 : 0);
        if (selectors != 1)
        {
            _logger.LogError("Specify exactly one selector: --solution-name OR --package-source.");
            return 1;
        }

        if (hasSolutionSelector && (!string.IsNullOrWhiteSpace(PackageSource) || !string.IsNullOrWhiteSpace(OutputDirectory) || !string.Equals(PackageVersion, "latest", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogError("Package-only options (--package-source/--version/--output) cannot be used with --solution-name.");
            return 1;
        }

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
                var uninstaller = new SolutionUninstaller(client, _logger);
                if (!string.IsNullOrWhiteSpace(SolutionName))
                {
                    var outcome = await uninstaller.UninstallByUniqueNameAsync(SolutionName).ConfigureAwait(false);
                    return RenderSingle(outcome);
                }

                return await RunPackageUninstallAsync(client, uninstaller).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "deploy uninstall failed");
                return 1;
            }
        }
    }

    private async Task<int> RunPackageUninstallAsync(ServiceClient client, SolutionUninstaller uninstaller)
    {
        var sourceReader = new PackageImportConfigReader(_logger);
        var importOrder = await sourceReader.ReadSolutionUniqueNamesInImportOrderAsync(
                PackageSource!,
                PackageVersion,
                OutputDirectory)
            .ConfigureAwait(false);

        var solutionNames = BuildReverseUninstallOrderFromImportConfig(importOrder);
        if (solutionNames.Count == 0)
        {
            _logger.LogError("No uninstallable solutions were resolved from package source '{Source}'.", PackageSource);
            return 1;
        }

        var packageDisplayName = InferPackageDisplayNameFromSource(PackageSource!);
        var outcomes = await ExecutePackageUninstallAsync(
                client,
                uninstaller,
                solutionNames,
                packageDisplayName,
                packageRunLabel: PackageSource!)
            .ConfigureAwait(false);

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "package",
                packageSource = PackageSource,
                packageName = packageDisplayName,
                solutionCount = solutionNames.Count,
                uninstallOrder = solutionNames,
                outcomes,
            }, JsonOptions));
        }
        else
        {
            OutputWriter.WriteLine($"Package: {packageDisplayName}");
            OutputWriter.WriteLine($"Package source: {PackageSource}");
            OutputWriter.WriteLine($"Resolved solutions: {solutionNames.Count}");
            OutputWriter.WriteLine("Uninstall order (reverse ImportConfig):");
            foreach (var name in solutionNames)
            {
                OutputWriter.WriteLine($"  - {name}");
            }
            foreach (var outcome in outcomes)
            {
                OutputWriter.WriteLine($"- {outcome.SolutionName}: {outcome.Status} ({outcome.Message})");
            }
        }

        return outcomes.All(o => o.Status == SolutionUninstallStatus.Success) ? 0 : 1;
    }

    private async Task<IReadOnlyList<SolutionUninstallOutcome>> ExecutePackageUninstallAsync(
        ServiceClient? client,
        SolutionUninstaller uninstaller,
        IReadOnlyList<string> solutionNames,
        string? packageDisplayName,
        string packageRunLabel)
    {
        Guid? historyId = null;
        int? successState = null;
        int? successStatus = null;
        int? failedState = null;
        int? failedStatus = null;
        if (client is not null)
        {
            var historyWriter = new PackageHistoryWriter(client, _logger);
            var statusCodes = await historyWriter.ResolveStatusCodesAsync().ConfigureAwait(false);
            successState = statusCodes.SuccessState;
            successStatus = statusCodes.SuccessStatus;
            failedState = statusCodes.FailedState;
            failedStatus = statusCodes.FailedStatus;

            var created = await historyWriter.TryCreateUninstallRunAsync(
                    uniqueName: packageDisplayName ?? packageRunLabel,
                    executionName: $"txc uninstall {packageRunLabel}",
                    statusCode: statusCodes.InProcessStatus,
                    message: $"Package uninstall started. {solutionNames.Count} solution(s) in reverse order.")
                .ConfigureAwait(false);
            historyId = created?.Id;
        }

        var outcomes = new List<SolutionUninstallOutcome>(solutionNames.Count);
        for (int i = 0; i < solutionNames.Count; i++)
        {
            var name = solutionNames[i];
            _logger.LogInformation("[{Current}/{Total}] Uninstalling solution {SolutionName}...", i + 1, solutionNames.Count, name);
            var outcome = await uninstaller.UninstallByUniqueNameAsync(name).ConfigureAwait(false);
            outcomes.Add(outcome);

            if (outcome.Status == SolutionUninstallStatus.Success)
            {
                _logger.LogInformation("[{Current}/{Total}] {SolutionName}: {Status}", i + 1, solutionNames.Count, outcome.SolutionName, outcome.Status);
            }
            else
            {
                _logger.LogWarning("[{Current}/{Total}] {SolutionName}: {Status} ({Message})", i + 1, solutionNames.Count, outcome.SolutionName, outcome.Status, outcome.Message);
            }
        }

        if (client is not null && historyId is { } id)
        {
            var historyWriter = new PackageHistoryWriter(client, _logger);
            bool allSuccess = outcomes.All(o => o.Status == SolutionUninstallStatus.Success);
            await historyWriter.TryUpdateStatusAsync(
                    id,
                    allSuccess ? successState : failedState,
                    allSuccess ? successStatus : failedStatus,
                    allSuccess
                        ? $"Package uninstall completed. {outcomes.Count} solution(s) uninstalled."
                        : $"Package uninstall completed with failures. {outcomes.Count(o => o.Status == SolutionUninstallStatus.Success)}/{outcomes.Count} succeeded.")
                .ConfigureAwait(false);
        }

        return outcomes;
    }

    private int RenderSingle(SolutionUninstallOutcome outcome)
    {
        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "solution",
                outcome,
            }, JsonOptions));
        }
        else
        {
            OutputWriter.WriteLine($"Solution: {outcome.SolutionName}");
            OutputWriter.WriteLine($"  status: {outcome.Status}");
            if (outcome.SolutionId is { } id)
            {
                OutputWriter.WriteLine($"  id: {id}");
            }
            OutputWriter.WriteLine($"  message: {outcome.Message}");
        }

        return outcome.Status == SolutionUninstallStatus.Success ? 0 : 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static IReadOnlyList<string> BuildReverseUninstallOrderFromImportConfig(IReadOnlyList<string> importOrderSolutionNames)
    {
        ArgumentNullException.ThrowIfNull(importOrderSolutionNames);

        var ordered = importOrderSolutionNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ordered.Reverse();
        return ordered;
    }

    private static string InferPackageDisplayNameFromSource(string packageSource)
    {
        if (string.IsNullOrWhiteSpace(packageSource))
        {
            return "(unknown)";
        }

        if (Directory.Exists(packageSource) || File.Exists(packageSource))
        {
            var name = Path.GetFileName(packageSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.EndsWith(".pdpkg.zip", StringComparison.OrdinalIgnoreCase))
            {
                return name[..^".pdpkg.zip".Length];
            }

            if (name.EndsWith(".pdpkg", StringComparison.OrdinalIgnoreCase))
            {
                return name[..^".pdpkg".Length];
            }

            return string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
        }

        return packageSource.Trim();
    }
}
