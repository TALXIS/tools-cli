using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Steps;

[CliReadOnly]
[CliCommand(
    Name = "show",
    Description = "Show the full configuration of a single plugin processing step — message, entity, stage, mode, rank, state, filtering attributes, and configuration. Accepts a step GUID or a step name (exact or unique substring)."
)]
public class PluginStepShowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginStepShowCliCommand));

    [CliArgument(Name = "step", Description = "Step id (GUID) or name (exact or unique substring).")]
    public string Step { get; set; } = null!;

    [CliOption(Name = "--assembly", Description = "Narrow the search to steps whose owning assembly name contains this substring (helps disambiguate names).", Required = false)]
    public string? Assembly { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPluginInventoryService>();
        var rows = await service.ListStepsAsync(Profile, Assembly, CancellationToken.None).ConfigureAwait(false);

        var resolution = PluginStepResolver.Resolve(rows, Step);
        if (resolution.Step is null)
        {
            Logger.LogError("{Error}", resolution.Error);
            return ExitValidationError;
        }

        OutputFormatter.WriteData(resolution.Step, PrintDetail);
        return ExitSuccess;
    }

    /// <summary>
    /// Builds the human-readable detail lines for a step. Kept pure and public
    /// so the rendering is unit-testable without a live environment.
    /// </summary>
    public static IReadOnlyList<string> BuildDetailLines(PluginStepRecord step)
    {
        var mode = step.Mode == PluginExecutionMode.Synchronous ? "Sync" : "Async";
        var state = step.Enabled ? "Enabled" : "Disabled";

        var lines = new List<string>
        {
            $"Name      : {step.Name}",
            $"Id        : {step.Id}",
            $"State     : {state}",
            $"Message   : {step.Message}",
            $"Entity    : {step.PrimaryEntity ?? "(none)"}",
            $"Stage     : {step.Stage}",
            $"Mode      : {mode}",
            $"Rank      : {step.Rank}",
            $"Type      : {step.PluginTypeName}",
            $"Assembly  : {step.AssemblyName}" + (step.AssemblyVersion is { } v ? $" ({v})" : ""),
        };

        if (!string.IsNullOrWhiteSpace(step.Description))
            lines.Add($"Description : {step.Description}");
        if (!string.IsNullOrWhiteSpace(step.FilteringAttributes))
            lines.Add($"Filtering Attributes : {step.FilteringAttributes}");
        if (!string.IsNullOrWhiteSpace(step.Configuration))
            lines.Add($"Configuration : {step.Configuration}");

        return lines;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintDetail(PluginStepRecord step)
    {
        foreach (var line in BuildDetailLines(step))
            OutputWriter.WriteLine(line);
    }
#pragma warning restore TXC003
}
