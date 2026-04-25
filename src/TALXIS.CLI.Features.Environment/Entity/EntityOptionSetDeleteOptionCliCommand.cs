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
/// Deletes an option value from a local or global option set.
/// Usage: <c>txc environment entity optionset delete-option --value &lt;int&gt; (--global-optionset &lt;name&gt; | --entity &lt;name&gt; --attribute &lt;name&gt;)</c>
/// </summary>
[CliCommand(
    Name = "delete-option",
    Description = "Delete an option value from a local or global option set."
)]
public class EntityOptionSetDeleteOptionCliCommand : StagedCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EntityOptionSetDeleteOptionCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity (for local option sets).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "The logical name of the attribute (for local option sets).", Required = false)]
    public string? Attribute { get; set; }

    [CliOption(Name = "--global-optionset", Description = "The name of the global option set (mutually exclusive with --entity/--attribute).", Required = false)]
    public string? GlobalOptionset { get; set; }

    [CliOption(Name = "--value", Description = "The integer value of the option to remove.", Required = true)]
    public int Value { get; set; }

    public async Task<int> RunAsync()
    {
        ValidateExecutionMode();

        // Validate mutually exclusive options.
        bool hasGlobal = !string.IsNullOrWhiteSpace(GlobalOptionset);
        bool hasLocal = !string.IsNullOrWhiteSpace(Entity) || !string.IsNullOrWhiteSpace(Attribute);

        if (hasGlobal && hasLocal)
        {
            _logger.LogError("Specify either --global-optionset or --entity/--attribute, not both.");
            return 1;
        }

        if (!hasGlobal && !hasLocal)
        {
            _logger.LogError("Specify --global-optionset for a global option set, or --entity and --attribute for a local one.");
            return 1;
        }

        if (hasLocal && (string.IsNullOrWhiteSpace(Entity) || string.IsNullOrWhiteSpace(Attribute)))
        {
            _logger.LogError("Both --entity and --attribute are required for local option sets.");
            return 1;
        }

        if (Stage)
        {
            string stageTarget = hasGlobal ? GlobalOptionset! : $"{Entity}.{Attribute}";
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "DELETE",
                TargetType = "optionset",
                TargetDescription = stageTarget,
                Details = $"remove option value: {Value}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["attribute"] = Attribute,
                    ["globalOptionset"] = GlobalOptionset,
                    ["value"] = Value
                }
            });
            OutputWriter.WriteLine($"Staged: DELETE option {Value} from {stageTarget}");
            return 0;
        }

        try
        {
            var service = TxcServices.Get<IDataverseOptionSetService>();
            await service.DeleteOptionAsync(
                Profile, Entity, Attribute, GlobalOptionset, Value, CancellationToken.None
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment entity optionset delete-option failed");
            return 1;
        }

        string target = hasGlobal ? $"global option set '{GlobalOptionset}'" : $"attribute '{Attribute}' on entity '{Entity}'";
        OutputWriter.WriteLine($"Option value {Value} removed from {target}.");
        return 0;
    }
}
