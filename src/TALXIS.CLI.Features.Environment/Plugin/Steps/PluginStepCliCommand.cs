using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Plugin.Steps;

[CliCommand(
    Name = "step",
    Description = "List, inspect, and toggle plugin processing steps registered in the connected environment.",
    Children = new[]
    {
        typeof(PluginStepListCliCommand),
        typeof(PluginStepShowCliCommand),
        typeof(PluginStepEnableCliCommand),
        typeof(PluginStepDisableCliCommand),
        typeof(PluginStepEnableAllCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PluginStepCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
