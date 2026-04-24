using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Retrieves a single record by entity logical name and record ID.
/// </summary>
[CliCommand(
    Name = "get",
    Description = "Retrieve a single record by ID."
)]
public class EnvDataRecordGetCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordGetCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(Description = "The GUID of the record to retrieve.")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--columns", Description = "Comma-separated column names to retrieve.", Required = false)]
    public string? Columns { get; set; }

    [CliOption(Name = "--include-annotations", Description = "Include formatted values / OData annotations.", Required = false)]
    public bool IncludeAnnotations { get; set; }

    [CliOption(Name = "--json", Description = "Emit the record as indented JSON.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        JsonElement result;
        try
        {
            var columns = string.IsNullOrWhiteSpace(Columns)
                ? null
                : Columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var service = TxcServices.Get<IDataverseRecordService>();
            result = await service.GetAsync(Profile, Entity, RecordId, columns, IncludeAnnotations, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "record get failed");
            return 1;
        }

        if (Json)
        {
            OutputWriter.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            PrintKeyValuePairs(result);
        }

        return 0;
    }

    private static void PrintKeyValuePairs(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            OutputWriter.WriteLine(element.GetRawText());
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            OutputWriter.WriteLine($"{property.Name} = {FormatValue(property.Value)}");
        }
    }

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "(null)",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "(null)",
        _ => value.GetRawText(),
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
