using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Parent command for option set operations.
/// Usage: <c>txc environment entity optionset [global|option]</c>
/// </summary>
[CliCommand(
    Name = "optionset",
    Description = "Create and manage option sets (choices).",
    Children = new[]
    {
        typeof(EntityOptionSetGlobalCliCommand),
        typeof(EntityOptionSetOptionCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EntityOptionSetCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Group command for global option set operations.
/// Usage: <c>txc environment entity optionset global [create|delete|list]</c>
/// </summary>
[CliCommand(
    Name = "global",
    Description = "Manage global option sets.",
    Children = new[]
    {
        typeof(EntityOptionSetCreateGlobalCliCommand),
        typeof(EntityOptionSetDeleteGlobalCliCommand),
        typeof(EntityOptionSetListGlobalCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EntityOptionSetGlobalCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}

/// <summary>
/// Group command for individual option (value) operations.
/// Usage: <c>txc environment entity optionset option [add|delete]</c>
/// </summary>
[CliCommand(
    Name = "option",
    Description = "Add or remove individual options (values) in an option set.",
    Children = new[]
    {
        typeof(EntityOptionSetAddOptionCliCommand),
        typeof(EntityOptionSetDeleteOptionCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class EntityOptionSetOptionCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
