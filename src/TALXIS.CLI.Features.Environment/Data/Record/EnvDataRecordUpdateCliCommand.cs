using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Updates a single record identified by entity logical name and record ID.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Update a single record by ID from JSON attributes."
)]
#pragma warning disable TXC003
public class EnvDataRecordUpdateCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordUpdateCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(
        Description = "The GUID of the record to update.",
        ValidationPattern = CliValidation.GuidPattern,
        ValidationMessage = CliValidation.GuidValidationMessage)]
    public required Guid RecordId { get; set; }

    [CliOption(Name = "--data", Description = "Inline JSON object with attributes to update.", Required = false)]
    public string? Data { get; set; }

    [CliOption(Name = "--file", Description = "Path to a JSON file containing attributes to update.", Required = false)]
    public string? File { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "data",
                OperationType = "UPDATE",
                TargetType = "record",
                TargetDescription = $"{Entity}/{RecordId}",
                Details = Data is not null ? "inline JSON" : $"file: {File}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["recordId"] = RecordId.ToString(),
                    ["data"] = Data,
                    ["file"] = File
                }
            });
            OutputWriter.WriteLine($"Staged: UPDATE record '{RecordId}' in '{Entity}'");
            return ExitSuccess;
        }

        if (!TryParseAttributes(out var attributes))
            return ExitValidationError;

        var service = TxcServices.Get<IDataverseRecordService>();
        await service.UpdateAsync(Profile, Entity, RecordId, attributes, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteResult("succeeded", "Record updated successfully.");
        return ExitSuccess;
    }

    private bool TryParseAttributes(out JsonElement attributes)
        => RecordInputHelper.TryParseAttributes(Data, File, Logger, out attributes);
}
