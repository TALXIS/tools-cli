using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Record;

/// <summary>
/// Links two records together through a many-to-many (N:N) relationship.
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "associate",
    Description = "Link two records together through a many-to-many (N:N) relationship. You need to know the relationship schema name."
)]
#pragma warning disable TXC003
public class EnvDataRecordAssociateCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordAssociateCliCommand));

    [CliArgument(
        Description = "The GUID of the source record.",
        ValidationPattern = CliValidation.GuidPattern,
        ValidationMessage = CliValidation.GuidValidationMessage)]
    public required Guid RecordId { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name of the source record.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--target", Description = "The GUID of the target record to associate.", Required = true,
        ValidationPattern = CliValidation.GuidPattern, ValidationMessage = CliValidation.GuidValidationMessage)]
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
                OperationType = "ASSOCIATE",
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
            OutputWriter.WriteLine($"Staged: ASSOCIATE {Entity}/{RecordId} with {TargetEntity}/{Target} via '{Relationship}'");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseRelationshipService>();
        await service.AssociateAsync(Profile, Entity, RecordId, TargetEntity, Target, Relationship, CancellationToken.None)
            .ConfigureAwait(false);

        OutputWriter.WriteLine($"Associated {Entity}/{RecordId} with {TargetEntity}/{Target} via '{Relationship}'");

        return ExitSuccess;
    }
}
