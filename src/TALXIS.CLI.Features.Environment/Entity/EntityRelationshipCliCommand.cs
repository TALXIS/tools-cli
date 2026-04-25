using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Parent command for entity relationship operations.
/// Usage: <c>txc environment entity relationship [create|list]</c>
/// </summary>
[CliCommand(
    Name = "relationship",
    Description = "Create and list entity relationships.",
    Children = new[] { typeof(EntityRelationshipCreateCliCommand), typeof(EntityRelationshipListCliCommand) }
)]
public class EntityRelationshipCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
