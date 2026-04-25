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
/// Deletes a single record by entity logical name and record ID.
/// </summary>
[CliCommand(
    Name = "delete",
    Description = "Delete a single record by ID."
)]
public class EnvDataRecordDeleteCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataRecordDeleteCliCommand));

    [CliOption(Name = "--entity", Description = "Entity logical name (e.g. account).", Required = true)]
    public string Entity { get; set; } = null!;

    [CliArgument(Description = "The GUID of the record to delete.")]
    public Guid RecordId { get; set; }

    public async Task<int> RunAsync()
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
            OutputWriter.WriteLine($"Staged: DELETE record '{RecordId}' from '{Entity}'");
            return 0;
        }

        try
        {
            var service = TxcServices.Get<IDataverseRecordService>();
            await service.DeleteAsync(Profile, Entity, RecordId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "record delete failed");
            return 1;
        }

        OutputWriter.WriteLine("Record deleted successfully.");
        return 0;
    }
}
