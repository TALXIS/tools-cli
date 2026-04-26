using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component;

[CliCommand(
    Name = "component",
    Alias = "comp",
    Description = "Inspect components independent of a specific solution (layers, dependencies).",
    Children = new[]
    {
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
