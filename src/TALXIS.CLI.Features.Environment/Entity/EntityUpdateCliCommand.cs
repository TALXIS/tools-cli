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
/// Updates entity-level metadata (display name, plural name, description).
/// Usage: <c>txc environment entity update --entity &lt;name&gt; [--display-name &lt;label&gt;] [--plural-name &lt;label&gt;] [--description &lt;text&gt;]</c>
/// </summary>
[CliCommand(
    Name = "update",
    Description = "Update entity-level metadata (display name, plural name, description)."
)]
public class EntityUpdateCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityUpdateCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity to update.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The new display name for the entity.", Required = false)]
    public string? DisplayName { get; set; }

    [CliOption(Name = "--plural-name", Description = "The new plural display name for the entity.", Required = false)]
    public string? PluralName { get; set; }

    [CliOption(Name = "--description", Description = "The new description for the entity.", Required = false)]
    public string? Description { get; set; }

    public async Task<int> RunAsync()
    {
        ValidateExecutionMode();

        if (DisplayName is null && PluralName is null && Description is null)
        {
            _logger.LogError("At least one of --display-name, --plural-name, or --description must be provided.");
            return 1;
        }

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "UPDATE",
                TargetType = "entity",
                TargetDescription = Entity,
                Details = string.Join(", ", new[] {
                    DisplayName is not null ? $"displayName: \"{DisplayName}\"" : null,
                    PluralName is not null ? $"pluralName: \"{PluralName}\"" : null,
                    Description is not null ? $"description: \"{Description}\"" : null
                }.Where(s => s is not null)),
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["displayName"] = DisplayName,
                    ["pluralName"] = PluralName,
                    ["description"] = Description
                }
            });
            OutputWriter.WriteLine($"Staged: UPDATE entity '{Entity}'");
            return 0;
        }

        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.UpdateEntityAsync(
                Profile, Entity, DisplayName, PluralName, Description, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity update failed");
            return 1;
        }

        OutputWriter.WriteLine($"Entity '{Entity}' updated successfully.");
        return 0;
    }
}
