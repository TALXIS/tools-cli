using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Lists entities in the connected Dataverse environment.
/// Usage: <c>txc environment entity list [--search &lt;term&gt;] [--include-system]</c>
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List entities in the target environment."
)]
public class EntityListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityListCliCommand));

    [CliOption(Name = "--search", Description = "Filter entities by logical name, schema name, or display name.", Required = false)]
    public string? Search { get; set; }

    [CliOption(Name = "--include-system", Description = "Include non-customizable system entities in the output.", Required = false)]
    public bool IncludeSystem { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IDataverseEntityMetadataService>();
        var rows = await service.ListEntitiesAsync(Profile, Search, IncludeSystem, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintEntitiesTable);
        return ExitSuccess;
    }

    private static void PrintEntitiesTable(IReadOnlyList<EntitySummaryRecord> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No entities found.");
            return;
        }

        int logicalWidth = Math.Clamp(rows.Max(r => r.LogicalName.Length), 12, 48);
        int schemaWidth = Math.Clamp(rows.Max(r => (r.SchemaName ?? "").Length), 11, 48);
        int displayWidth = Math.Clamp(rows.Max(r => (r.DisplayName ?? "").Length), 12, 40);
        int entitySetWidth = Math.Clamp(rows.Max(r => (r.EntitySetName ?? "").Length), 14, 48);
        int customWidth = 6;

        string header =
            $"{"Logical Name".PadRight(logicalWidth)} | " +
            $"{"Schema Name".PadRight(schemaWidth)} | " +
            $"{"Display Name".PadRight(displayWidth)} | " +
            $"{"Entity Set Name".PadRight(entitySetWidth)} | " +
            $"{"Custom".PadRight(customWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var r in rows)
        {
            string logical = Truncate(r.LogicalName, logicalWidth);
            string schema = Truncate(r.SchemaName ?? "", schemaWidth);
            string display = Truncate(r.DisplayName ?? "", displayWidth);
            string entitySet = Truncate(r.EntitySetName ?? "", entitySetWidth);
            string custom = r.IsCustomEntity ? "true" : "false";

            OutputWriter.WriteLine(
                $"{logical.PadRight(logicalWidth)} | " +
                $"{schema.PadRight(schemaWidth)} | " +
                $"{display.PadRight(displayWidth)} | " +
                $"{entitySet.PadRight(entitySetWidth)} | " +
                $"{custom.PadRight(customWidth)}");
        }
    }

    /// <summary>Truncate a string to fit the column width, appending a dot if trimmed.</summary>
    private static string Truncate(string value, int maxWidth) =>
        value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;

}
