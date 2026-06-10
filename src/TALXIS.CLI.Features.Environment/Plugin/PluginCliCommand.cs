using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Plugin;

[CliCommand(
    Name = "plugin",
    Description = "Inspect plugin assemblies, plugin types, and processing steps registered in the connected environment.",
    Children = new[]
    {
        typeof(Assemblies.PluginAssemblyCliCommand),
        typeof(Types.PluginTypeCliCommand),
        typeof(Steps.PluginStepCliCommand),
        typeof(Profile.PluginProfileCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PluginCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
