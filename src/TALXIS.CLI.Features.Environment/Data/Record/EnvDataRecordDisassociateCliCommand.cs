using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Removes the link between two records in a many-to-many (N:N) relationship.
/// </summary>
[CliDestructive("Removes the association between two records.")]
[CliCommand(
    Name = "disassociate",
    Description = "Remove the link between two records in a many-to-many (N:N) relationship."
)]
#pragma warning disable TXC003
public class EnvDataRecordDisassociateCliCommand : StagedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordDisassociateCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliArgument(
        Description = "The GUID of the source record.",
        ValidationPattern = @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        ValidationMessage = "Value must be a valid GUID (e.g. 00000000-0000-0000-0000-000000000000).")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name of the source record.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--target", Description = "The GUID of the target record to disassociate.", Required = true)]
    public Guid Target { get; set; }

    [CliOption(Name = "--target-entity", Description = "Entity logical name of the target record.", Required = true)]
    public string TargetEntity { get; set; } = null!;

    [CliOption(Name = "--relationship", Description = "Schema name of the N:N relationship.", Required = true)]
    public string Relationship { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "data",
                OperationType = "DISASSOCIATE",
                TargetType = "record",
                TargetDescription = $"{Entity}/{RecordId} -> {TargetEntity}/{Target}",
                Details = $"relationship: {Relationship}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["recordId"] = RecordId.ToString(),
                    ["targetEntity"] = TargetEntity,
                    ["target"] = Target.ToString(),
                    ["relationship"] = Relationship
                }
            });
            OutputWriter.WriteLine($"Staged: DISASSOCIATE {Entity}/{RecordId} from {TargetEntity}/{Target} via '{Relationship}'");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseRelationshipService>();
        await service.DisassociateAsync(Profile, Entity, RecordId, TargetEntity, Target, Relationship, CancellationToken.None)
            .ConfigureAwait(false);

        OutputWriter.WriteLine($"Disassociated {Entity}/{RecordId} from {TargetEntity}/{Target} via '{Relationship}'");

        return ExitSuccess;
    }
}
