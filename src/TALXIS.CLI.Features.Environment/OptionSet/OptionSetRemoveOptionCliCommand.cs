using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.OptionSet;

/// <summary>
/// Removes an option value from a global or local option set.
/// Global: <c>txc environment optionset remove-option --name &lt;schema-name&gt; --value &lt;int&gt;</c>
/// Local:  <c>txc environment optionset remove-option --entity &lt;name&gt; --attribute &lt;name&gt; --value &lt;int&gt;</c>
/// </summary>
[CliDestructive("Permanently removes the option value from the option set.")]
[CliCommand(
    Name = "remove",
    Description = "Remove an option value from a global or local option set."
)]
#pragma warning disable TXC003
public class OptionSetRemoveOptionCliCommand : StagedCliCommand, IDestructiveCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(OptionSetRemoveOptionCliCommand));

    [CliOption(Name = "--yes", Description = "Skip interactive confirmation.", Required = false)]
    public bool Yes { get; set; }

    [CliOption(Name = "--name", Description = "Schema name of the global option set.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name (for local option sets).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "Attribute logical name (for local option sets).", Required = false)]
    public string? Attribute { get; set; }

    [CliOption(Name = "--value", Description = "Integer value of the option to remove.", Required = true)]
    public int Value { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        bool hasGlobal = !string.IsNullOrWhiteSpace(Name);
        bool hasLocal = !string.IsNullOrWhiteSpace(Entity) || !string.IsNullOrWhiteSpace(Attribute);

        if (hasGlobal && hasLocal)
        {
            Logger.LogError("Specify either --name (global) or --entity + --attribute (local), not both.");
            return ExitError;
        }
        if (!hasGlobal && !hasLocal)
        {
            Logger.LogError("Specify --name for a global option set, or --entity and --attribute for a local one.");
            return ExitError;
        }
        if (hasLocal && (string.IsNullOrWhiteSpace(Entity) || string.IsNullOrWhiteSpace(Attribute)))
        {
            Logger.LogError("Both --entity and --attribute are required for local option sets.");
            return ExitError;
        }

        if (Stage)
        {
            string stageTarget = hasGlobal ? Name! : $"{Entity}.{Attribute}";
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "DELETE",
                TargetType = "optionset-option",
                TargetDescription = stageTarget,
                Details = $"remove option value: {Value}",
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["attribute"] = Attribute,
                    ["name"] = Name,
                    ["value"] = Value
                }
            });
            OutputWriter.WriteLine($"Staged: REMOVE option {Value} from {stageTarget}");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseOptionSetService>();
        await service.DeleteOptionAsync(
            Profile, Entity, Attribute, Name, Value, CancellationToken.None
        ).ConfigureAwait(false);

        string target = hasGlobal ? $"global option set '{Name}'" : $"attribute '{Attribute}' on entity '{Entity}'";
        OutputWriter.WriteLine($"Option value {Value} removed from {target}.");
        return ExitSuccess;
    }
}
