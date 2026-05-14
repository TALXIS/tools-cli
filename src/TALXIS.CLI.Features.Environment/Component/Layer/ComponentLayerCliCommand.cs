using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component.Layer;

[CliCommand(
    Name = "layer",
    Description = "Inspect solution layers for a component.",
    Children = new[]
    {
        typeof(ComponentLayerListCliCommand),
        typeof(ComponentLayerGetCliCommand),
        typeof(ComponentLayerRemoveCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ComponentLayerCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
