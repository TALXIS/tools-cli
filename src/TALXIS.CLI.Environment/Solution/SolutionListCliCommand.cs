using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Commands.Abstractions;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Environment.Platforms.Dataverse;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Environment.Solution;

[CliCommand(
    Name = "list",
    Description = "List installed solutions in the target environment."
)]
public class SolutionListCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(SolutionListCliCommand));

    [CliOption(Name = "--managed", Description = "Filter installed solutions by managed status (true/false).", Required = false)]
    public string? Managed { get; set; }

    [CliOption(Name = "--json", Description = "Emit the list as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        bool? managedFilter = null;
        if (!string.IsNullOrWhiteSpace(Managed))
        {
            if (!bool.TryParse(Managed, out var parsedManaged))
            {
                _logger.LogError("Invalid --managed value '{Value}'. Use true or false.", Managed);
                return 1;
            }
            managedFilter = parsedManaged;
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
            try
            {
                var reader = new SolutionInventoryReader(conn.Client);
                var rows = await reader.ListAsync(managedFilter).ConfigureAwait(false);

                if (Json)
                {
                    OutputWriter.WriteLine(JsonSerializer.Serialize(rows, JsonOptions));
                    return 0;
                }

                PrintSolutionsTable(rows);
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "environment solution list failed");
                return 1;
            }
        }
    }

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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
