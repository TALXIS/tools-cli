using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Plugin.Steps;

[CliCommand(
    Name = "step",
    Description = "List plugin processing steps registered in the connected environment.",
    Children = new[] { typeof(PluginStepListCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PluginStepCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
