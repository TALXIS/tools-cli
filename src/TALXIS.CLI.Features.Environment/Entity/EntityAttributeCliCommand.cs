using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Parent command for entity attribute operations.
/// Usage: <c>txc environment entity attribute [create]</c>
/// </summary>
[CliCommand(
    Name = "attribute",
    Description = "Create and manage entity attributes (columns).",
    Children = new[] { typeof(EntityAttributeCreateCliCommand), typeof(EntityAttributeUpdateCliCommand), typeof(EntityAttributeDeleteCliCommand) }
)]
public class EntityAttributeCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
