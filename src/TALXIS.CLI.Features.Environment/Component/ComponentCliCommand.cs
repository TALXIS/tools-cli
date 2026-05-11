using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component;

[CliCommand(
    Name = "component",
    Alias = "comp",
    Description = "Inspect and navigate components in the live environment (layers, dependencies, browser editor).",
    Children = new[]
    {
        typeof(Browse.ComponentBrowseCliCommand),
        typeof(Layer.ComponentLayerCliCommand),
        typeof(Dependency.ComponentDependencyCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
