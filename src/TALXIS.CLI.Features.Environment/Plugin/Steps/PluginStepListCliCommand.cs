using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Steps;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List plugin processing steps (SdkMessageProcessingStep) registered in the connected environment. Shows which message + entity each step fires on, its stage, mode, rank, and enabled state."
)]
public class PluginStepListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginStepListCliCommand));

    [CliOption(Name = "--assembly", Description = "Filter to steps whose owning assembly name contains this substring.", Required = false)]
    public string? Assembly { get; set; }

    [CliOption(Name = "--entity", Description = "Filter to steps whose primary entity name contains this substring.", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--stage", Description = "Filter by execution stage: pre, post, prevalidation, preoperation, or postoperation.", Required = false)]
    public string? Stage { get; set; }

    [CliOption(Name = "--disabled", Description = "Show only disabled steps.", Required = false)]
    public bool DisabledOnly { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (!PluginStepQuery.TryParseStageFilter(Stage, out var stages, out var stageError))
        {
            Logger.LogError("{Error}", stageError);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IPluginInventoryService>();
        var rows = await service.ListStepsAsync(Profile, Assembly, CancellationToken.None).ConfigureAwait(false);

        var filtered = PluginStepQuery.Filter(rows, Entity, stages, DisabledOnly);

        OutputFormatter.WriteList(filtered, PrintTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<PluginStepRecord> rows)
    {
        if (rows.Count == 0) { OutputWriter.WriteLine("No plugin steps found."); return; }

        var ordered = rows
            .OrderBy(r => r.AssemblyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PluginTypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Rank)
            .ToList();

        int msgWidth = Math.Clamp(ordered.Max(r => r.Message.Length), 8, 18);
        int entityWidth = Math.Clamp(ordered.Max(r => (r.PrimaryEntity ?? "").Length), 8, 22);
        int typeWidth = Math.Clamp(ordered.Max(r => r.PluginTypeName.Length), 30, 60);

        string header = $"{"Message".PadRight(msgWidth)} | {"Entity".PadRight(entityWidth)} | {"Stage".PadRight(15)} | {"Mode".PadRight(5)} | {"Rank".PadRight(4)} | {"State".PadRight(8)} | {"Plugin Type".PadRight(typeWidth)} | Step Name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in ordered)
        {
            string entity = (r.PrimaryEntity ?? "").PadRight(entityWidth);
            string typeName = r.PluginTypeName.Length > typeWidth ? r.PluginTypeName[..(typeWidth - 1)] + "." : r.PluginTypeName;
            string state = r.Enabled ? "Enabled" : "Disabled";
            string mode = r.Mode == PluginExecutionMode.Synchronous ? "Sync" : "Async";
            OutputWriter.WriteLine($"{r.Message.PadRight(msgWidth)} | {entity} | {r.Stage.ToString().PadRight(15)} | {mode.PadRight(5)} | {r.Rank.ToString().PadRight(4)} | {state.PadRight(8)} | {typeName.PadRight(typeWidth)} | {r.Name}");
        }
    }
#pragma warning restore TXC003
}
