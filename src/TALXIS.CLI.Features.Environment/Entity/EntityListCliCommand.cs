using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Lists entities in the connected Dataverse environment.
/// Usage: <c>txc environment entity list [--search &lt;term&gt;] [--include-system] [--json]</c>
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List entities in the target environment."
)]
public class EntityListCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityListCliCommand));

    [CliOption(Name = "--search", Description = "Filter entities by logical name, schema name, or display name.", Required = false)]
    public string? Search { get; set; }

    [CliOption(Name = "--include-system", Description = "Include non-customizable system entities in the output.", Required = false)]
    public bool IncludeSystem { get; set; }

    [CliOption(Name = "--json", Description = "Emit the list as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        IReadOnlyList<EntitySummaryRecord> rows;
        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            rows = await service.ListEntitiesAsync(Profile, Search, IncludeSystem, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity list failed");
            return 1;
        }

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(rows, JsonOptions));
            return 0;
        }

        PrintEntitiesTable(rows);
        return 0;
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
