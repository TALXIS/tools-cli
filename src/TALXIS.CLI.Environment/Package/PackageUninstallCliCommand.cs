using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Commands.Abstractions;
using TALXIS.CLI.Config.Platforms.Dataverse;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Config.Providers.Dataverse.Platforms;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Environment.Package;

[CliCommand(
    Name = "uninstall",
    Description = "Uninstall all solutions belonging to a package from the target environment, in reverse import order."
)]
public class PackageUninstallCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(PackageUninstallCliCommand));

    [CliArgument(Name = "package", Description = "NuGet package name, local .pdpkg.zip/.pdpkg/.zip archive path, or extracted package folder path.", Required = true)]
    public required string Package { get; set; }

    [CliOption(Name = "--version", Description = "NuGet package version when 'package' is a NuGet name. Defaults to 'latest'.", Required = false)]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Directory for temporary/downloaded package assets when resolving from NuGet.", Required = false)]
    public string? OutputDirectory { get; set; }

    [CliOption(Name = "--yes", Description = "Confirm destructive uninstall actions.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--json", Description = "Emit uninstall result as JSON.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        if (!Yes)
        {
            _logger.LogError("Uninstall is destructive. Pass --yes to confirm.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(Package))
        {
            _logger.LogError("'package' argument is required.");
            return 1;
        }

        DataverseConnection conn;
        try
        {
            conn = await DataverseCommandBridge.ConnectAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
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
                return await RunPackageUninstallAsync(client, uninstaller).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "environment package uninstall failed");
                return 1;
            }
        }
    }

    private async Task<int> RunPackageUninstallAsync(ServiceClient client, SolutionUninstaller uninstaller)
    {
        var sourceReader = new PackageImportConfigReader();
        var importOrder = await sourceReader.ReadSolutionUniqueNamesInImportOrderAsync(
                Package,
                PackageVersion,
                OutputDirectory)
            .ConfigureAwait(false);

        var solutionNames = BuildReverseUninstallOrderFromImportConfig(importOrder);
        if (solutionNames.Count == 0)
        {
            _logger.LogError("No uninstallable solutions were resolved from package '{Source}'.", Package);
            return 1;
        }

        var packageDisplayName = InferPackageDisplayNameFromSource(Package);
        var outcomes = await ExecutePackageUninstallAsync(
                client,
                uninstaller,
                solutionNames,
                packageDisplayName,
                packageRunLabel: Package)
            .ConfigureAwait(false);

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "package",
                package = Package,
                packageName = packageDisplayName,
                solutionCount = solutionNames.Count,
                uninstallOrder = solutionNames,
                outcomes,
            }, JsonOptions));
        }
        else
        {
            OutputWriter.WriteLine($"Package: {packageDisplayName}");
            OutputWriter.WriteLine($"Source: {Package}");
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
