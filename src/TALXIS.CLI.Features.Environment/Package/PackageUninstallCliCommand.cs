using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Package;

[CliCommand(
    Name = "uninstall",
    Description = "Uninstall all solutions belonging to a package from the target environment, in reverse import order."
)]
[McpIgnore] // Destructive operation requiring --yes confirmation
public class PackageUninstallCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PackageUninstallCliCommand));

    [CliArgument(Name = "package", Description = "NuGet package name, local .pdpkg.zip/.pdpkg/.zip archive path, or extracted package folder path.", Required = true)]
    public required string Package { get; set; }

    [CliOption(Name = "--version", Description = "NuGet package version when 'package' is a NuGet name. Defaults to 'latest'.", Required = false)]
    public string PackageVersion { get; set; } = "latest";

    [CliOption(Name = "--output", Aliases = ["-o"], Description = "Directory for temporary/downloaded package assets when resolving from NuGet.", Required = false)]
    public string? OutputDirectory { get; set; }

    [CliOption(Name = "--yes", Description = "Confirm destructive uninstall actions.", Required = false)]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!Yes)
        {
            Logger.LogError("Uninstall is destructive. Pass --yes to confirm.");
            return ExitValidationError;
        }

        if (string.IsNullOrWhiteSpace(Package))
        {
            Logger.LogError("'package' argument is required.");
            return ExitValidationError;
        }

        var service = TxcServices.Get<IPackageUninstallService>();
        var result = await service.UninstallAsync(new PackageUninstallRequest(
            ProfileName: Profile,
            PackageSource: Package,
            PackageVersion: PackageVersion,
            OutputDirectory: OutputDirectory), CancellationToken.None).ConfigureAwait(false);

        if (result.UninstallOrder.Count == 0)
        {
            Logger.LogError("No uninstallable solutions were resolved from package '{Source}'.", Package);
            return ExitError;
        }

        if (OutputContext.IsJson)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "package",
                package = Package,
                packageName = result.PackageDisplayName,
                solutionCount = result.UninstallOrder.Count,
                uninstallOrder = result.UninstallOrder,
                outcomes = result.Outcomes,
            }, TxcOutputJsonOptions.Default));
        }
        else
        {
            OutputWriter.WriteLine($"Package: {result.PackageDisplayName}");
            OutputWriter.WriteLine($"Source: {Package}");
            OutputWriter.WriteLine($"Resolved solutions: {result.UninstallOrder.Count}");
            OutputWriter.WriteLine("Uninstall order (reverse ImportConfig):");
            foreach (var name in result.UninstallOrder)
            {
                OutputWriter.WriteLine($"  - {name}");
            }
            foreach (var outcome in result.Outcomes)
            {
                OutputWriter.WriteLine($"- {outcome.SolutionName}: {outcome.Status} ({outcome.Message})");
            }
        }

        return result.Outcomes.All(o => o.Status == SolutionUninstallStatus.Success) ? ExitSuccess : ExitError;
    }

    /// <summary>
    /// Pure helper kept for test coverage: derives the reverse uninstall order from an
    /// import-order list (trim, distinct, reverse). The service uses equivalent logic.
    /// </summary>
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
}
