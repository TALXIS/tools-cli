using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
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
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordGetCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(Description = "The GUID of the record to retrieve.")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--columns", Description = "Comma-separated column names to retrieve.", Required = false)]
    public string? Columns { get; set; }

    [CliOption(Name = "--include-annotations", Description = "Include formatted values / OData annotations.", Required = false)]
    public bool IncludeAnnotations { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var columns = string.IsNullOrWhiteSpace(Columns)
            ? null
            : Columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var service = TxcServices.Get<IDataverseRecordService>();
        var result = await service.GetAsync(Profile, Entity, RecordId, columns, IncludeAnnotations, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(result, PrintKeyValuePairs);
        return ExitSuccess;
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

}
