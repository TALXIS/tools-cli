using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.OptionSet;

/// <summary>
/// Parent command for option set (choice) operations.
/// Covers both global option sets and local (entity-attribute) option sets.
/// Usage: <c>txc environment optionset [list|describe|create|delete|add-option|remove-option]</c>
/// </summary>
[CliCommand(
    Name = "optionset",
    Description = "Manage option sets (choices) — global and local.",
    Children = new[]
    {
        typeof(OptionSetListCliCommand),
        typeof(OptionSetShowCliCommand),
        typeof(OptionSetCreateCliCommand),
        typeof(OptionSetDeleteCliCommand),
        typeof(OptionSetOptionCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class OptionSetCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Sub-resource for individual option values within an option set.
/// Usage: <c>txc environment optionset option [add|remove]</c>
/// </summary>
[CliCommand(
    Name = "option",
    Description = "Add or remove individual values in an option set.",
    Children = new[]
    {
        typeof(OptionSetAddOptionCliCommand),
        typeof(OptionSetRemoveOptionCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class OptionSetOptionCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
