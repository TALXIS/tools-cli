using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Updates entity-level metadata (display name, plural name, description).
/// Usage: <c>txc environment entity update --entity &lt;name&gt; [--display-name &lt;label&gt;] [--plural-name &lt;label&gt;] [--description &lt;text&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Update entity-level metadata (display name, plural name, description)."
)]
#pragma warning disable TXC003
public class EntityUpdateCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityUpdateCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity to update.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The new display name for the entity.", Required = false)]
    public string? DisplayName { get; set; }

    [CliOption(Name = "--plural-name", Description = "The new plural display name for the entity.", Required = false)]
    public string? PluralName { get; set; }

    [CliOption(Name = "--description", Description = "The new description for the entity.", Required = false)]
    public string? Description { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (DisplayName is null && PluralName is null && Description is null)
        {
            Logger.LogError("At least one of --display-name, --plural-name, or --description must be provided.");
            return ExitError;
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
            return ExitSuccess;
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
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "environment entity update failed");
            return ExitError;
        }

        OutputWriter.WriteLine($"Entity '{Entity}' updated successfully.");
        return ExitSuccess;
    }
}
