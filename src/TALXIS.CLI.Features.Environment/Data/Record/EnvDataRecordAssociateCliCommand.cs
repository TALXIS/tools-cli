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
/// Links two records together through a many-to-many (N:N) relationship.
/// </summary>
[CliCommand(
    Name = "associate",
    Description = "Link two records together through a many-to-many (N:N) relationship. You need to know the relationship schema name."
)]
public class EnvDataRecordAssociateCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordAssociateCliCommand));

    [CliArgument(Description = "The GUID of the source record.")]
    public Guid RecordId { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name of the source record.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--target", Description = "The GUID of the target record to associate.", Required = true)]
    public Guid Target { get; set; }

    [CliOption(Name = "--target-entity", Description = "Entity logical name of the target record.", Required = true)]
    public string TargetEntity { get; set; } = null!;

    [CliOption(Name = "--relationship", Description = "Schema name of the N:N relationship.", Required = true)]
    public string Relationship { get; set; } = null!;

    public async Task<int> RunAsync()
    {
        try
        {
            var service = TxcServices.Get<IDataverseRelationshipService>();
            await service.AssociateAsync(Profile, Entity, RecordId, TargetEntity, Target, Relationship, CancellationToken.None)
                .ConfigureAwait(false);

            OutputWriter.WriteLine($"Associated {Entity}/{RecordId} with {TargetEntity}/{Target} via '{Relationship}'");
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "record associate failed");
            return 1;
        }

        return 0;
    }
}
