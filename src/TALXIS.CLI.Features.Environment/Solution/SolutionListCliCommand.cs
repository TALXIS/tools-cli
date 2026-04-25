using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliCommand(
    Name = "list",
    Description = "List installed solutions in the target environment."
)]
public class SolutionListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionListCliCommand));

    [CliOption(Name = "--managed", Description = "Filter installed solutions by managed status (true/false).", Required = false)]
    public string? Managed { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        bool? managedFilter = null;
        if (!string.IsNullOrWhiteSpace(Managed))
        {
            if (!bool.TryParse(Managed, out var parsedManaged))
            {
                Logger.LogError("Invalid --managed value '{Value}'. Use true or false.", Managed);
                return ExitValidationError;
            }
            managedFilter = parsedManaged;
        }

        var service = TxcServices.Get<ISolutionInventoryService>();
        var rows = await service.ListAsync(Profile, managedFilter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintSolutionsTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintSolutionsTable(IReadOnlyList<InstalledSolutionRecord> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No installed solutions found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.UniqueName.Length), 20, 48);
        int versionWidth = Math.Clamp(rows.Max(r => (r.Version ?? "").Length), 7, 20);
        int managedWidth = 7;

        string header = $"{"Unique Name".PadRight(nameWidth)} | {"Version".PadRight(versionWidth)} | {"Managed".PadRight(managedWidth)} | Friendly Name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string uniqueName = r.UniqueName.Length > nameWidth
                ? r.UniqueName[..(nameWidth - 1)] + "."
                : r.UniqueName;
            string version = string.IsNullOrWhiteSpace(r.Version) ? "(unknown)" : r.Version;
            string friendly = string.IsNullOrWhiteSpace(r.FriendlyName) ? "(none)" : r.FriendlyName;
            OutputWriter.WriteLine($"{uniqueName.PadRight(nameWidth)} | {version.PadRight(versionWidth)} | {(r.Managed ? "true" : "false").PadRight(managedWidth)} | {friendly}");
        }
    }
#pragma warning restore TXC003

}
