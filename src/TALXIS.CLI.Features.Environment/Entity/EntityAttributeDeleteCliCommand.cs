using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Deletes an attribute (column) from a Dataverse entity.
/// Usage: <c>txc environment entity attribute delete --entity &lt;name&gt; --name &lt;name&gt;</c>
/// </summary>
[CliDestructive("Permanently deletes the attribute from the remote environment.")]
[CliCommand(
    Name = "delete",
    Description = "Delete an attribute (column) from an entity."
)]
#pragma warning disable TXC003
public class EntityAttributeDeleteCliCommand : StagedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--entity", Description = "The logical name of the entity.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--name", Description = "The logical name of the attribute to delete.", Required = true)]
    public string Name { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
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
            return ExitSuccess;
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
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "environment entity attribute delete failed");
            return ExitError;
        }

        OutputWriter.WriteLine($"Attribute '{Name}' deleted from entity '{Entity}'.");
        return ExitSuccess;
    }
}
