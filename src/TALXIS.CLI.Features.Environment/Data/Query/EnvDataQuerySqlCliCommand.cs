using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Query;

/// <summary>
/// Executes a T-SQL subset query against the Dataverse environment via
/// the Web API <c>?sql=</c> parameter.
/// </summary>
/// <example>
///   txc environment data query sql "SELECT name, accountnumber FROM account WHERE statecode = 0"
///   txc env data query sql "SELECT TOP 10 fullname FROM contact" --json
/// </example>
[CliCommand(
    Name = "sql",
    Description = "Execute a SQL query against the Dataverse environment."
)]
public class EnvDataQuerySqlCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataQuerySqlCliCommand));

    [CliArgument(Description = "The SQL query to execute (SELECT only).")]
    public string Sql { get; set; } = string.Empty;

    [CliOption(Name = "--top", Description = "Maximum number of records to return.", Required = false)]
    public int? Top { get; set; }

    [CliOption(Name = "--include-annotations", Description = "Include OData annotations in the output.", Required = false)]
    public bool IncludeAnnotations { get; set; }

    [CliOption(Name = "--json", Description = "Emit the result as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        DataverseQueryResult result;
        try
        {
            var service = TxcServices.Get<IDataverseQueryService>();
            result = await service.QuerySqlAsync(Profile, Sql, Top, IncludeAnnotations, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment data query sql failed");
            return 1;
        }

        OutputQueryResult(result, Json);
        return 0;
    }

    /// <summary>
    /// Outputs query results as JSON or as a dynamic text table.
    /// Shared across all query leaf commands.
    /// </summary>
    internal static void OutputQueryResult(DataverseQueryResult result, bool json)
    {
        if (json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(result.Records, JsonOptions));
            return;
        }

        if (result.Records.Count == 0)
        {
            OutputWriter.WriteLine("No records returned.");
            return;
        }

        PrintDynamicTable(result.Records);
    }

    /// <summary>
    /// Builds a text table whose columns are derived from the keys of the
    /// first record in the result set.
    /// </summary>
    private static void PrintDynamicTable(IReadOnlyList<JsonElement> records)
    {
        // Collect column names from the first record.
        var columns = new List<string>();
        if (records[0].ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in records[0].EnumerateObject())
                columns.Add(prop.Name);
        }

        if (columns.Count == 0)
        {
            // Fallback: just dump as JSON lines.
            foreach (var rec in records)
                OutputWriter.WriteLine(rec.ToString());
            return;
        }

        // Compute column widths (header length vs. max value length, capped at 40).
        var widths = new int[columns.Count];
        for (int c = 0; c < columns.Count; c++)
        {
            widths[c] = columns[c].Length;
            foreach (var rec in records)
            {
                var val = GetCellValue(rec, columns[c]);
                if (val.Length > widths[c])
                    widths[c] = val.Length;
            }
            widths[c] = Math.Clamp(widths[c], columns[c].Length, 40);
        }

        // Header row.
        var header = string.Join(" | ", columns.Select((col, i) => col.PadRight(widths[i])));
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        // Data rows.
        foreach (var rec in records)
        {
            var cells = columns.Select((col, i) =>
            {
                var val = GetCellValue(rec, col);
                return val.Length > widths[i]
                    ? val[..(widths[i] - 1)] + "."
                    : val.PadRight(widths[i]);
            });
            OutputWriter.WriteLine(string.Join(" | ", cells));
        }

        OutputWriter.WriteLine($"({records.Count} record{(records.Count == 1 ? "" : "s")})");
    }

    private static string GetCellValue(JsonElement record, string column)
    {
        if (record.ValueKind != JsonValueKind.Object)
            return string.Empty;
        if (!record.TryGetProperty(column, out var prop))
            return string.Empty;
        return prop.ValueKind switch
        {
            JsonValueKind.Null => "(null)",
            JsonValueKind.String => prop.GetString() ?? string.Empty,
            _ => prop.ToString(),
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
