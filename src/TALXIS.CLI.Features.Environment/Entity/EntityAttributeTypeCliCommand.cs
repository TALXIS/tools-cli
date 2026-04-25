using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Entity;

/// <summary>
/// Parent command for attribute type introspection.
/// Usage: <c>txc environment entity attribute type [list|describe]</c>
/// </summary>
[CliCommand(
    Name = "type",
    Description = "Discover available attribute types and their parameters.",
    Children = new[] { typeof(EntityAttributeTypeListCliCommand), typeof(EntityAttributeTypeDescribeCliCommand) }
)]
public class EntityAttributeTypeCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
