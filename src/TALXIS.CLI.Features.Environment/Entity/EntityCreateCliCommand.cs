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
/// Creates a new entity (table) in Dataverse.
/// Usage: <c>txc environment entity create --name &lt;schema-name&gt; --display-name &lt;label&gt; --plural-name &lt;label&gt; [--description &lt;text&gt;] [--solution &lt;name&gt;]</c>
/// </summary>
[CliCommand(
    Name = "create",
    Description = "Create a new entity (table) in the environment."
)]
public class EntityCreateCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityCreateCliCommand));

    [CliOption(Name = "--name", Description = "The schema name of the new entity.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The display name (label) for the entity.", Required = true)]
    public string DisplayName { get; set; } = null!;

    [CliOption(Name = "--plural-name", Description = "The plural display name (label) for the entity.", Required = true)]
    public string PluralName { get; set; } = null!;

    [CliOption(Name = "--description", Description = "The description for the entity.", Required = false)]
    public string? Description { get; set; }

    [CliOption(Name = "--solution", Description = "The unique name of the solution to add the entity to.", Required = false)]
    public string? Solution { get; set; }

    public async Task<int> RunAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "CREATE",
                TargetType = "entity",
                TargetDescription = Name,
                Details = $"display: \"{DisplayName}\", plural: \"{PluralName}\"",
                Parameters = new Dictionary<string, object?>
                {
                    ["name"] = Name,
                    ["displayName"] = DisplayName,
                    ["pluralName"] = PluralName,
                    ["description"] = Description,
                    ["solution"] = Solution
                }
            });
            OutputWriter.WriteLine($"Staged: CREATE entity '{Name}'");
            return 0;
        }

        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.CreateEntityAsync(
                Profile, Name, DisplayName, PluralName, Description, Solution, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity create failed");
            return 1;
        }

        OutputWriter.WriteLine($"Entity '{Name}' created successfully.");
        return 0;
    }
}
