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
/// Deletes an attribute (column) from a Dataverse entity.
/// Usage: <c>txc environment entity attribute delete --entity &lt;name&gt; --name &lt;name&gt;</c>
/// </summary>
[CliCommand(
    Name = "delete",
    Description = "Delete an attribute (column) from an entity."
)]
public class EntityAttributeDeleteCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeDeleteCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--name", Description = "The logical name of the attribute to delete.", Required = true)]
    public string Name { get; set; } = null!;

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
                TargetType = "attribute",
                TargetDescription = $"{Entity}.{Name}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["name"] = Name
                }
            });
            OutputWriter.WriteLine($"Staged: DELETE attribute '{Entity}.{Name}'");
            return 0;
        }

        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.DeleteAttributeAsync(
                Profile, Entity, Name, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity attribute delete failed");
            return 1;
        }

        OutputWriter.WriteLine($"Attribute '{Name}' deleted from entity '{Entity}'.");
        return 0;
    }
}
