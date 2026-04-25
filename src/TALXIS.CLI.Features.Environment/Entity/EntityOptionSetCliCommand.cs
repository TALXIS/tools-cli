using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Parent command for option set operations.
/// Usage: <c>txc environment entity optionset [create-global|add-option|delete-option|list-global]</c>
/// </summary>
[CliCommand(
    Name = "optionset",
    Description = "Create and manage option sets (choices).",
    Children = new[]
    {
        typeof(EntityOptionSetCreateGlobalCliCommand),
        typeof(EntityOptionSetDeleteGlobalCliCommand),
        typeof(EntityOptionSetAddOptionCliCommand),
        typeof(EntityOptionSetDeleteOptionCliCommand),
        typeof(EntityOptionSetListGlobalCliCommand)
    }
)]
public class EntityOptionSetCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
