using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Steps;

[CliIdempotent]
[CliCommand(
    Name = "enable-all",
    Description = "Enable every disabled plugin processing step for an assembly in one go. Useful right after importing a solution whose steps came in disabled. Already-enabled steps are left untouched."
)]
public class PluginStepEnableAllCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginStepEnableAllCliCommand));

    [CliOption(Name = "--assembly", Description = "Assembly name (substring) whose steps should be enabled.", Required = true)]
    public string Assembly { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPluginInventoryService>();
        var rows = await service.ListStepsAsync(Profile, Assembly, CancellationToken.None).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            OutputFormatter.WriteResult("succeeded", $"No plugin steps found for assembly matching '{Assembly}'.");
            return ExitSuccess;
        }

        var toEnable = SelectDisabledStepIds(rows);
        if (toEnable.Count == 0)
        {
            OutputFormatter.WriteResult("succeeded", $"All {rows.Count} step(s) for '{Assembly}' are already enabled.");
            return ExitSuccess;
        }

        var count = await service.SetStepsStateAsync(Profile, toEnable, enabled: true, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteResult("succeeded", $"Enabled {count} step(s) for assembly matching '{Assembly}'.");
        return ExitSuccess;
    }

    /// <summary>
    /// Selects the ids of steps that are currently disabled. Kept pure and
    /// public so the "don't touch already-enabled steps" rule is unit-testable.
    /// </summary>
    public static IReadOnlyList<Guid> SelectDisabledStepIds(IReadOnlyList<PluginStepRecord> rows)
        => rows.Where(r => !r.Enabled).Select(r => r.Id).ToList();
}
