using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Updates an existing attribute (column) on a Dataverse entity.
/// Usage: <c>txc environment entity attribute update --entity &lt;name&gt; --name &lt;name&gt; [--display-name &lt;label&gt;] [--description &lt;text&gt;] [--required &lt;none|recommended|required&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "update",
    Description = "Update an existing attribute (column) on an entity."
)]
#pragma warning disable TXC003
public class EntityAttributeUpdateCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityAttributeUpdateCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity.", Required = true)]
    public string Entity { get; set; } = null!;

    [CliOption(Name = "--name", Description = "The logical name of the attribute to update.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--display-name", Description = "The new display name (label) for the attribute.", Required = false)]
    public string? DisplayName { get; set; }

    [CliOption(Name = "--description", Description = "The new description for the attribute.", Required = false)]
    public string? Description { get; set; }

    [CliOption(Name = "--required", Description = "The required level: none, recommended, required.", Required = false)]
    public string? Required { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        if (Stage)
        {
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "UPDATE",
                TargetType = "attribute",
                TargetDescription = $"{Entity}.{Name}",
                Details = string.Join(", ", new[] {
                    DisplayName is not null ? $"displayName: \"{DisplayName}\"" : null,
                    Description is not null ? $"description: \"{Description}\"" : null,
                    Required is not null ? $"required: {Required}" : null
                }.Where(s => s is not null)),
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["name"] = Name,
                    ["displayName"] = DisplayName,
                    ["description"] = Description,
                    ["required"] = Required
                }
            });
            OutputWriter.WriteLine($"Staged: UPDATE attribute '{Entity}.{Name}'");
            return ExitSuccess;
        }

        try
        {
            var service = TxcServices.Get<IDataverseEntityMetadataService>();
            await service.UpdateAttributeAsync(
                Profile, Entity, Name, DisplayName, Description, Required, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or ArgumentException)
        {
            Logger.LogError("{Error}", ex.Message);
            return ExitError;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "environment entity attribute update failed");
            return ExitError;
        }

        OutputWriter.WriteLine($"Attribute '{Name}' on entity '{Entity}' updated successfully.");
        return ExitSuccess;
    }
}
