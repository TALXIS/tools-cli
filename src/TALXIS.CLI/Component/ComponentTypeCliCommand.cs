using DotMake.CommandLine;

namespace TALXIS.CLI.Component;

/// <summary>
/// Component type subgroup — list and explain registered component types.
/// </summary>
[CliCommand(
    Name = "type",
    Description = "Discover available component types, their aliases, and metadata.",
    Children = new[] { typeof(ComponentTypeListCliCommand), typeof(ComponentTypeExplainCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ComponentTypeCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
