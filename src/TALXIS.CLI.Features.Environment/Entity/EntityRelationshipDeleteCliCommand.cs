using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Deletes a relationship from Dataverse by its schema name.
/// Usage: <c>txc environment entity relationship delete --name &lt;relationship-schema-name&gt;</c>
/// </summary>
[CliDestructive("Permanently deletes the relationship from the remote environment.")]
[CliCommand(
    Name = "delete",
    Description = "Delete a relationship by its schema name."
)]
#pragma warning disable TXC003
public class EntityRelationshipDeleteCliCommand : StagedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityRelationshipDeleteCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation for this destructive operation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--name", Description = "The schema name of the relationship to delete.", Required = true)]
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
                TargetType = "relationship",
                TargetDescription = Name,
                Parameters = new Dictionary<string, object?>
                {
                    ["name"] = Name
                }
            });
            OutputWriter.WriteLine($"Staged: DELETE relationship '{Name}'");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseEntityMetadataService>();
        await service.DeleteRelationshipAsync(
            Profile, Name, CancellationToken.None
        ).ConfigureAwait(false);

        OutputWriter.WriteLine($"Relationship '{Name}' deleted successfully.");
        return ExitSuccess;
    }
}
