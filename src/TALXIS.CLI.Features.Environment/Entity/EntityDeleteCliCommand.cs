using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Deletes an entity (table) from Dataverse.
/// Usage: <c>txc environment entity delete --entity &lt;name&gt;</c>
/// </summary>
[CliCommand(
    Name = "delete",
    Description = "Delete an entity (table) from the environment."
)]
public class EntityDeleteCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityDeleteCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity to delete.", Required = true)]
    public string Entity { get; set; } = null!;

    public async Task<int> RunAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "DELETE",
                TargetType = "entity",
                TargetDescription = Entity,
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity
                }
            });
            OutputWriter.WriteLine($"Staged: DELETE entity '{Entity}'");
            return 0;
        }

        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.DeleteEntityAsync(
                Profile, Entity, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity delete failed");
            return 1;
        }

        OutputWriter.WriteLine($"Entity '{Entity}' deleted successfully.");
        return 0;
    }
}
