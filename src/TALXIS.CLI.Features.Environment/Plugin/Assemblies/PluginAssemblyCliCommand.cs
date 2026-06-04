using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Plugin.Assemblies;

[CliCommand(
    Name = "assembly",
    Description = "List and inspect plugin assemblies registered in the connected environment.",
    Children = new[]
    {
        typeof(PluginAssemblyListCliCommand),
        typeof(PluginAssemblyShowCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PluginAssemblyCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
