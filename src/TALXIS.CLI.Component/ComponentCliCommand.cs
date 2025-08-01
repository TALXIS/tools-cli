using DotMake.CommandLine;
namespace TALXIS.CLI.Component;

[CliCommand(
    Description = "Component scaffolding utilities for TALXIS solutions.",
    Children = new[] {
        typeof(AppComponentCommand),
        typeof(ListTemplatesCliCommand),
    }
)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
