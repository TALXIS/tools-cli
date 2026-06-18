using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Assemblies;

[CliReadOnly]
[CliCommand(
    Name = "show",
    Description = "Show the details of a single plugin assembly — its plugin types, processing steps (with enabled/disabled state), and step images. Accepts an assembly GUID or name (exact or unique substring)."
)]
public class PluginAssemblyShowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginAssemblyShowCliCommand));

    [CliArgument(Name = "assembly", Description = "Assembly id (GUID) or name (exact or unique substring).")]
    public string Assembly { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPluginInventoryService>();

        var assemblies = await service.ListAssembliesAsync(Profile, Assembly, CancellationToken.None).ConfigureAwait(false);
        var resolution = PluginAssemblyResolver.Resolve(assemblies, Assembly);
        if (resolution.Assembly is null)
        {
            Logger.LogError("{Error}", resolution.Error);
            return ExitValidationError;
        }

        var asm = resolution.Assembly;
        var types = await service.ListTypesAsync(Profile, asm.Name, kind: null, CancellationToken.None).ConfigureAwait(false);
        var steps = await service.ListStepsAsync(Profile, asm.Name, CancellationToken.None).ConfigureAwait(false);
        var images = await service.ListStepImagesAsync(Profile, asm.Name, CancellationToken.None).ConfigureAwait(false);

        // Keep only the rows that actually belong to the resolved assembly — the
        // service filters are substring matches, so a narrower id match can still
        // pull in siblings sharing a name fragment.
        var ownTypes = types.Where(t => t.AssemblyId == asm.Id).ToList();
        var ownSteps = steps.Where(s => s.AssemblyId == asm.Id).ToList();
        var stepIds = ownSteps.Select(s => s.Id).ToHashSet();
        var ownImages = images.Where(i => stepIds.Contains(i.StepId)).ToList();

        var detail = new { Assembly = asm, Types = ownTypes, Steps = ownSteps, Images = ownImages };
        OutputFormatter.WriteData(detail, _ => PrintDetail(asm, ownTypes, ownSteps, ownImages));
        return ExitSuccess;
    }

    /// <summary>
    /// Builds the human-readable detail block for an assembly and its components.
    /// Pure and public so rendering is unit-testable without a live environment.
    /// </summary>
    public static IReadOnlyList<string> BuildDetailLines(
        PluginAssemblyRecord asm,
        IReadOnlyList<PluginTypeRecord> types,
        IReadOnlyList<PluginStepRecord> steps,
        IReadOnlyList<PluginStepImageRecord> images)
    {
        var imagesByStep = images
            .GroupBy(i => i.StepId)
            .ToDictionary(g => g.Key, g => g.Count());

        var lines = new List<string>
        {
            $"Assembly  : {asm.Name}" + (asm.Version is { } v ? $" ({v})" : ""),
            $"Id        : {asm.Id}",
            $"Isolation : {asm.IsolationMode}",
            $"Source    : {asm.SourceType}",
        };
        if (asm.ModifiedOn is { } modified)
            lines.Add($"Modified  : {modified:yyyy-MM-dd HH:mm}");

        lines.Add("");
        lines.Add($"Types ({types.Count}):");
        foreach (var t in types.OrderBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase))
            lines.Add($"  {t.TypeName} [{t.Kind}]");

        lines.Add("");
        lines.Add($"Steps ({steps.Count}):");
        foreach (var s in steps
            .OrderBy(s => s.PluginTypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Rank))
        {
            var state = s.Enabled ? "Enabled" : "Disabled";
            var entity = s.PrimaryEntity ?? "(none)";
            var imageCount = imagesByStep.TryGetValue(s.Id, out var c) ? c : 0;
            var imagePart = imageCount > 0 ? $", {imageCount} image(s)" : "";
            lines.Add($"  [{state}] {s.Message} of {entity} ({s.Stage}{imagePart}) — {s.Name}");
        }

        return lines;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintDetail(
        PluginAssemblyRecord asm,
        IReadOnlyList<PluginTypeRecord> types,
        IReadOnlyList<PluginStepRecord> steps,
        IReadOnlyList<PluginStepImageRecord> images)
    {
        foreach (var line in BuildDetailLines(asm, types, steps, images))
            OutputWriter.WriteLine(line);
    }
#pragma warning restore TXC003
}
