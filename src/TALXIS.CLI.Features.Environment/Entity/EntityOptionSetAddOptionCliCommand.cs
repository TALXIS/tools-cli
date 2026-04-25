using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Adds an option value to a local or global option set.
/// Usage: <c>txc environment entity optionset add-option --label &lt;text&gt; (--global-optionset &lt;name&gt; | --entity &lt;name&gt; --attribute &lt;name&gt;) [--value &lt;int&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "add-option",
    Description = "Add an option value to a local or global option set."
)]
#pragma warning disable TXC003
public class EntityOptionSetAddOptionCliCommand : StagedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EntityOptionSetAddOptionCliCommand));

    [CliOption(Name = "--entity", Description = "The logical name of the entity (for local option sets).", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--attribute", Description = "The logical name of the attribute (for local option sets).", Required = false)]
    public string? Attribute { get; set; }

    [CliOption(Name = "--global-optionset", Description = "The name of the global option set (mutually exclusive with --entity/--attribute).", Required = false)]
    public string? GlobalOptionset { get; set; }

    [CliOption(Name = "--label", Description = "The label for the new option.", Required = true)]
    public string Label { get; set; } = null!;

    [CliOption(Name = "--value", Description = "The integer value for the new option (auto-generated if not provided).", Required = false)]
    public int? Value { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        ValidateExecutionMode();

        // Validate mutually exclusive options.
        bool hasGlobal = !string.IsNullOrWhiteSpace(GlobalOptionset);
        bool hasLocal = !string.IsNullOrWhiteSpace(Entity) || !string.IsNullOrWhiteSpace(Attribute);

        if (hasGlobal && hasLocal)
        {
            Logger.LogError("Specify either --global-optionset or --entity/--attribute, not both.");
            return ExitError;
        }

        if (!hasGlobal && !hasLocal)
        {
            Logger.LogError("Specify --global-optionset for a global option set, or --entity and --attribute for a local one.");
            return ExitError;
        }

        if (hasLocal && (string.IsNullOrWhiteSpace(Entity) || string.IsNullOrWhiteSpace(Attribute)))
        {
            Logger.LogError("Both --entity and --attribute are required for local option sets.");
            return ExitError;
        }

        if (Stage)
        {
            string stageTarget = hasGlobal ? GlobalOptionset! : $"{Entity}.{Attribute}";
            var store = TxcServices.Get<IChangesetStore>();
            store.Add(new StagedOperation
            {
                Category = "schema",
                OperationType = "CREATE",
                TargetType = "optionset",
                TargetDescription = stageTarget,
                Details = $"add option: \"{Label}\"" + (Value.HasValue ? $" ({Value})" : ""),
                Parameters = new Dictionary<string, object?>
                {
                    ["entity"] = Entity,
                    ["attribute"] = Attribute,
                    ["globalOptionset"] = GlobalOptionset,
                    ["label"] = Label,
                    ["value"] = Value
                }
            });
            OutputWriter.WriteLine($"Staged: ADD option '{Label}' to {stageTarget}");
            return ExitSuccess;
        }

        var service = TxcServices.Get<IDataverseOptionSetService>();
        await service.InsertOptionAsync(
            Profile, Entity, Attribute, GlobalOptionset, Label, Value, CancellationToken.None
        ).ConfigureAwait(false);

        string target = hasGlobal ? $"global option set '{GlobalOptionset}'" : $"attribute '{Attribute}' on entity '{Entity}'";
        OutputWriter.WriteLine($"Option '{Label}' added to {target}.");
        return ExitSuccess;
    }
}
