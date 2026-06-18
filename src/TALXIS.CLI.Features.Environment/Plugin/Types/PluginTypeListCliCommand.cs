using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Types;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List plugin types (plugins and workflow activities) registered in the connected environment."
)]
public class PluginTypeListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginTypeListCliCommand));

    [CliOption(Name = "--assembly", Description = "Filter to plugin types whose parent assembly name contains this substring.", Required = false)]
    public string? Assembly { get; set; }

    [CliOption(Name = "--kind", Description = "Filter by kind: plugin, workflow, or all (default).", Required = false)]
    public string? Kind { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        PluginKind? kind = null;
        if (!string.IsNullOrWhiteSpace(Kind))
        {
            switch (Kind.Trim().ToLowerInvariant())
            {
                case "plugin": kind = PluginKind.Plugin; break;
                case "workflow":
                case "workflowactivity":
                case "wf": kind = PluginKind.WorkflowActivity; break;
                case "all": kind = null; break;
                default:
                    Logger.LogError("Invalid --kind value '{Kind}'. Expected: plugin, workflow, or all.", Kind);
                    return ExitValidationError;
            }
        }

        var service = TxcServices.Get<IPluginInventoryService>();
        var rows = await service.ListTypesAsync(Profile, Assembly, kind, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<PluginTypeRecord> rows)
    {
        if (rows.Count == 0) { OutputWriter.WriteLine("No plugin types found."); return; }

        int typeWidth = Math.Clamp(rows.Max(r => r.TypeName.Length), 30, 70);
        int assemblyWidth = Math.Clamp(rows.Max(r => r.AssemblyName.Length), 15, 45);
        int versionWidth = Math.Clamp(rows.Max(r => (r.AssemblyVersion ?? "").Length), 7, 15);
        string header = $"{"Type Name".PadRight(typeWidth)} | {"Kind".PadRight(16)} | {"Assembly".PadRight(assemblyWidth)} | {"Version".PadRight(versionWidth)} | WF Group";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string typeName = r.TypeName.Length > typeWidth ? r.TypeName[..(typeWidth - 1)] + "." : r.TypeName;
            string asm = r.AssemblyName.Length > assemblyWidth ? r.AssemblyName[..(assemblyWidth - 1)] + "." : r.AssemblyName;
            string ver = (r.AssemblyVersion ?? "").PadRight(versionWidth);
            string group = r.WorkflowActivityGroupName ?? "";
            OutputWriter.WriteLine($"{typeName.PadRight(typeWidth)} | {r.Kind.ToString().PadRight(16)} | {asm.PadRight(assemblyWidth)} | {ver} | {group}");
        }
    }
#pragma warning restore TXC003
}
