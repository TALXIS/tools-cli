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
/// Removes the link between two records in a many-to-many (N:N) relationship.
/// </summary>
[CliCommand(
    Name = "disassociate",
    Description = "Remove the link between two records in a many-to-many (N:N) relationship."
)]
public class EnvDataRecordDisassociateCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordDisassociateCliCommand));

    [CliArgument(Description = "The GUID of the source record.")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name of the source record.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--target", Description = "The GUID of the target record to disassociate.", Required = true)]
    public Guid Target { get; set; }

    [CliOption(Name = "--target-entity", Description = "Entity logical name of the target record.", Required = true)]
    public string TargetEntity { get; set; } = null!;

    [CliOption(Name = "--relationship", Description = "Schema name of the N:N relationship.", Required = true)]
    public string Relationship { get; set; } = null!;

    public async Task<int> RunAsync()
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
            return 0;
        }

        try
        {
            var service = TxcServices.Get<IDataverseRelationshipService>();
            await service.DisassociateAsync(Profile, Entity, RecordId, TargetEntity, Target, Relationship, CancellationToken.None)
                .ConfigureAwait(false);

            OutputWriter.WriteLine($"Disassociated {Entity}/{RecordId} from {TargetEntity}/{Target} via '{Relationship}'");
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "record disassociate failed");
            return 1;
        }

        return 0;
    }
}
