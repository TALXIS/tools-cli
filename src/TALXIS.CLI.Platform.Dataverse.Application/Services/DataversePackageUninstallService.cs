using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataversePackageUninstallService : IPackageUninstallService
{
    public async Task<PackageUninstallResult> UninstallAsync(PackageUninstallRequest request, CancellationToken ct)
    {
        var logger = TxcLoggerFactory.CreateLogger(nameof(DataversePackageUninstallService));

        var reader = new PackageImportConfigReader();
        var importOrder = await reader.ReadSolutionUniqueNamesInImportOrderAsync(
                request.PackageSource,
                request.PackageVersion,
                request.OutputDirectory)
            .ConfigureAwait(false);

        var solutionNames = BuildReverseUninstallOrder(importOrder);
        var packageDisplayName = InferPackageDisplayNameFromSource(request.PackageSource);

        if (solutionNames.Count == 0)
        {
            return new PackageUninstallResult(packageDisplayName, solutionNames, Array.Empty<SolutionUninstallOutcome>());
        }

        using var conn = await DataverseCommandBridge.ConnectAsync(request.ProfileName, ct).ConfigureAwait(false);
        var client = conn.Client;
        var uninstaller = new SolutionUninstaller(client, logger);

        var historyWriter = new PackageHistoryWriter(client, logger);
        var statusCodes = await historyWriter.ResolveStatusCodesAsync().ConfigureAwait(false);
        var created = await historyWriter.TryCreateUninstallRunAsync(
                uniqueName: packageDisplayName,
                executionName: $"txc uninstall {request.PackageSource}",
                statusCode: statusCodes.InProcessStatus,
                message: $"Package uninstall started. {solutionNames.Count} solution(s) in reverse order.")
            .ConfigureAwait(false);

        var outcomes = new List<SolutionUninstallOutcome>(solutionNames.Count);
        for (int i = 0; i < solutionNames.Count; i++)
        {
            var name = solutionNames[i];
            logger.LogInformation("[{Current}/{Total}] Uninstalling solution {SolutionName}...", i + 1, solutionNames.Count, name);
            var outcome = await uninstaller.UninstallByUniqueNameAsync(name).ConfigureAwait(false);
            outcomes.Add(outcome);

            if (outcome.Status == SolutionUninstallStatus.Success)
            {
                logger.LogInformation("[{Current}/{Total}] {SolutionName}: {Status}", i + 1, solutionNames.Count, outcome.SolutionName, outcome.Status);
            }
            else
            {
                logger.LogWarning("[{Current}/{Total}] {SolutionName}: {Status} ({Message})", i + 1, solutionNames.Count, outcome.SolutionName, outcome.Status, outcome.Message);
            }
        }

        if (created?.Id is { } id)
        {
            bool allSuccess = outcomes.All(o => o.Status == SolutionUninstallStatus.Success);
            await historyWriter.TryUpdateStatusAsync(
                    id,
                    allSuccess ? statusCodes.SuccessState : statusCodes.FailedState,
                    allSuccess ? statusCodes.SuccessStatus : statusCodes.FailedStatus,
                    allSuccess
                        ? $"Package uninstall completed. {outcomes.Count} solution(s) uninstalled."
                        : $"Package uninstall completed with failures. {outcomes.Count(o => o.Status == SolutionUninstallStatus.Success)}/{outcomes.Count} succeeded.")
                .ConfigureAwait(false);
        }

        return new PackageUninstallResult(packageDisplayName, solutionNames, outcomes);
    }

    private static IReadOnlyList<string> BuildReverseUninstallOrder(IReadOnlyList<string> importOrder)
    {
        var ordered = importOrder
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        ordered.Reverse();
        return ordered;
    }

    private static string InferPackageDisplayNameFromSource(string packageSource)
    {
        if (string.IsNullOrWhiteSpace(packageSource)) return "(unknown)";

        if (Directory.Exists(packageSource) || File.Exists(packageSource))
        {
            var name = Path.GetFileName(packageSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.EndsWith(".pdpkg.zip", StringComparison.OrdinalIgnoreCase)) return name[..^".pdpkg.zip".Length];
            if (name.EndsWith(".pdpkg", StringComparison.OrdinalIgnoreCase)) return name[..^".pdpkg".Length];
            return string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
        }

        return packageSource.Trim();
    }
}
