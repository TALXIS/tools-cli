using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Plugin.Assemblies;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List plugin assemblies registered in the connected environment. Useful for verifying which version of an assembly is currently deployed."
)]
public class PluginAssemblyListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(PluginAssemblyListCliCommand));

    [CliOption(Name = "--name", Description = "Filter to assemblies whose name contains this substring.", Required = false)]
    public string? Name { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPluginInventoryService>();
        var rows = await service.ListAssembliesAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<PluginAssemblyRecord> rows)
    {
        if (rows.Count == 0) { OutputWriter.WriteLine("No plugin assemblies found."); return; }

        int nameWidth = Math.Clamp(rows.Max(r => r.Name.Length), 20, 60);
        int versionWidth = Math.Clamp(rows.Max(r => (r.Version ?? "").Length), 7, 18);
        string header = $"{"Name".PadRight(nameWidth)} | {"Version".PadRight(versionWidth)} | {"Isolation".PadRight(9)} | {"Source".PadRight(8)} | Modified";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string name = r.Name.Length > nameWidth ? r.Name[..(nameWidth - 1)] + "." : r.Name;
            string version = (r.Version ?? "").PadRight(versionWidth);
            string modified = r.ModifiedOn?.ToString("yyyy-MM-dd HH:mm") ?? "";
            OutputWriter.WriteLine($"{name.PadRight(nameWidth)} | {version} | {r.IsolationMode.ToString().PadRight(9)} | {r.SourceType.ToString().PadRight(8)} | {modified}");
        }
    }
#pragma warning restore TXC003
}
