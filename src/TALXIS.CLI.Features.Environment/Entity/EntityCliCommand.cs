using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Parent command for entity discovery and schema introspection.
/// Usage: <c>txc environment entity [list|describe]</c>
/// </summary>
[CliCommand(
    Name = "entity",
    Description = "Entity discovery and schema metadata for the live environment.",
    Children = new[] { typeof(EntityListCliCommand), typeof(EntityDescribeCliCommand), typeof(EntityGetCliCommand), typeof(EntityUpdateCliCommand), typeof(EntityCreateCliCommand), typeof(EntityDeleteCliCommand), typeof(EntityAttributeCliCommand), typeof(EntityRelationshipCliCommand), typeof(EntityOptionSetCliCommand) }
)]
public class EntityCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
