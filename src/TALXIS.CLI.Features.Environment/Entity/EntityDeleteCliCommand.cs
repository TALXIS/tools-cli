using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Deletes an entity (table) from Dataverse.
/// Usage: <c>txc environment entity delete --entity &lt;name&gt;</c>
/// </summary>
[CliDestructive("Permanently deletes the entity from the remote environment.")]
[CliCommand(
    Name = "delete",
    Description = "Delete an entity (table) from the environment."
)]
#pragma warning disable TXC003
public class EntityDeleteCliCommand : StagedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--entity", Description = "The logical name of the entity to delete.", Required = true)]
    public string Entity { get; set; } = null!;

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
                TargetType = "entity",
                TargetDescription = Entity,
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity
                }
            });
            OutputWriter.WriteLine($"Staged: DELETE entity '{Entity}'");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseEntityMetadataService>();
        await service.DeleteEntityAsync(
            Profile, Entity, CancellationToken.None
        ).ConfigureAwait(false);

        OutputWriter.WriteLine($"Entity '{Entity}' deleted successfully.");
        return ExitSuccess;
    }
}
