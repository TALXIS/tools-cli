using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Creates a many-to-many (N:N) relationship between two entities.
/// Usage: <c>txc environment entity relationship create --entity1 &lt;name&gt; --entity2 &lt;name&gt; --name &lt;schema-name&gt; [--display-name &lt;label&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a many-to-many (N:N) relationship between two entities."
)]
#pragma warning disable TXC003
public class EntityRelationshipCreateCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityRelationshipCreateCliCommand));

    [CliOption(Name = "--entity1", Description = "The logical name of the first entity.", Required = true)]
    public string Entity1 { get; set; } = null!;

    [CliOption(Name = "--entity2", Description = "The logical name of the second entity.", Required = true)]
    public string Entity2 { get; set; } = null!;

    [CliOption(Name = "--name", Description = "The schema name for the relationship and intersect entity.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The display name (label) for the relationship menu items.", Required = false)]
    public string? DisplayName { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "CREATE",
                TargetType = "relationship",
                TargetDescription = Name,
                Details = $"{Entity1} <-> {Entity2}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity1"] = Entity1,
                    ["entity2"] = Entity2,
                    ["name"] = Name,
                    ["displayName"] = DisplayName
                }
            });
            OutputWriter.WriteLine($"Staged: CREATE relationship '{Name}' ({Entity1} <-> {Entity2})");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseEntityMetadataService>();
        await service.CreateManyToManyRelationshipAsync(
            Profile, Entity1, Entity2, Name, DisplayName, CancellationToken.None
        ).ConfigureAwait(false);

        OutputWriter.WriteLine($"Many-to-many relationship '{Name}' created between '{Entity1}' and '{Entity2}'.");
        return ExitSuccess;
    }
}
