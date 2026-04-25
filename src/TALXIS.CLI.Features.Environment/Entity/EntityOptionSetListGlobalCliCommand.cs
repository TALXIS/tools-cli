using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Lists all global option sets in the environment.
/// Usage: <c>txc environment entity optionset list-global [--format json]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list-global",
    Description = "List all global option sets in the environment."
)]
#pragma warning disable TXC003
public class EntityOptionSetListGlobalCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityOptionSetListGlobalCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        IReadOnlyList<GlobalOptionSetSummaryRecord> rows;
        try
        {
            var service = TxcServices.Get<IDataverseOptionSetService>();
            rows = await service.ListGlobalOptionSetsAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "environment entity optionset list-global failed");
            return ExitError;
        }

        OutputFormatter.WriteList(rows, PrintOptionSetsTable);
        return ExitSuccess;
    }

    private static void PrintOptionSetsTable(IReadOnlyList<GlobalOptionSetSummaryRecord> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No global option sets found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.Name.Length), 4, 60);
        int displayWidth = Math.Clamp(rows.Max(r => (r.DisplayName ?? "").Length), 12, 48);
        int typeWidth = Math.Clamp(rows.Max(r => r.OptionSetType.Length), 4, 16);
        int countWidth = 7; // "Options"
        int customWidth = 6; // "Custom"

        string header =
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Display Name".PadRight(displayWidth)} | " +
            $"{"Type".PadRight(typeWidth)} | " +
            $"{"Options".PadRight(countWidth)} | " +
            $"{"Custom".PadRight(customWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var r in rows)
        {
            string name = Truncate(r.Name, nameWidth);
            string display = Truncate(r.DisplayName ?? "", displayWidth);
            string type = Truncate(r.OptionSetType, typeWidth);
            string count = r.OptionCount.ToString();
            string custom = r.IsCustomOptionSet ? "true" : "false";

            OutputWriter.WriteLine(
                $"{name.PadRight(nameWidth)} | " +
                $"{display.PadRight(displayWidth)} | " +
                $"{type.PadRight(typeWidth)} | " +
                $"{count.PadRight(countWidth)} | " +
                $"{custom.PadRight(customWidth)}");
        }
    }

    /// <summary>Truncate a string to fit the column width, appending a dot if trimmed.</summary>
    private static string Truncate(string value, int maxWidth) =>
        value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
