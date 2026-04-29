using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.OptionSet;

/// <summary>
/// Adds an option value to a global or local option set.
/// Global: <c>txc environment optionset add-option --name &lt;schema-name&gt; --label &lt;text&gt;</c>
/// Local:  <c>txc environment optionset add-option --entity &lt;name&gt; --attribute &lt;name&gt; --label &lt;text&gt;</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Add an option value to a global or local option set."
)]
#pragma warning disable TXC003
public class OptionSetAddOptionCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(OptionSetAddOptionCliCommand));

    [CliOption(Name = "--name", Description = "Schema name of the global option set.", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name (for local option sets).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "Attribute logical name (for local option sets).", Required = false)]
    public string? Attribute { get; set; }

    [CliOption(Name = "--label", Description = "Label for the new option.", Required = true)]
    public string Label { get; set; } = null!;

    [CliOption(Name = "--value", Description = "Integer value for the new option (auto-generated if omitted).", Required = false)]
    public int? Value { get; set; }

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
                OperationType = "CREATE",
                TargetType = "optionset-option",
                TargetDescription = stageTarget,
                Details = $"add option: \"{Label}\"" + (Value.HasValue ? $" ({Value})" : ""),
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["attribute"] = Attribute,
                    ["name"] = Name,
                    ["label"] = Label,
                    ["value"] = Value
                }
            });
            OutputWriter.WriteLine($"Staged: ADD option '{Label}' to {stageTarget}");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseOptionSetService>();
        await service.InsertOptionAsync(
            Profile, Entity, Attribute, Name, Label, Value, CancellationToken.None
        ).ConfigureAwait(false);

        string target = hasGlobal ? $"global option set '{Name}'" : $"attribute '{Attribute}' on entity '{Entity}'";
        OutputWriter.WriteLine($"Option '{Label}' added to {target}.");
        return ExitSuccess;
    }
}
