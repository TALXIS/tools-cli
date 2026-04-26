using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Deletes a single record by entity logical name and record ID.
/// </summary>
[CliDestructive("Permanently deletes the record from the remote environment.")]
[CliCommand(
    Name = "delete",
    Description = "Delete a single record by ID."
)]
public class EnvDataRecordDeleteCliCommand : StagedCliCommand, IDestructiveCommand
{
    [CliOption(Name = "--yes", Description = "Skip confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordDeleteCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(
        Description = "The GUID of the record to delete.",
        ValidationPattern = CliValidation.GuidPattern,
        ValidationMessage = CliValidation.GuidValidationMessage)]
    public Guid RecordId { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "data",
                OperationType = "DELETE",
                TargetType = "record",
                TargetDescription = $"{Entity}/{RecordId}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["recordId"] = RecordId.ToString()
                }
            });
            OutputFormatter.WriteResult("staged", $"Staged: DELETE record '{RecordId}' from '{Entity}'");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseRecordService>();
        await service.DeleteAsync(Profile, Entity, RecordId, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteResult("succeeded", "Record deleted successfully.");
        return ExitSuccess;
    }
}
