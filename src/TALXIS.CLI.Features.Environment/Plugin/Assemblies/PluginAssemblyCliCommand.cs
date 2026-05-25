using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Plugin.Assemblies;

[CliCommand(
    Name = "assembly",
    Description = "List plugin assemblies registered in the connected environment.",
    Children = new[] { typeof(PluginAssemblyListCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PluginAssemblyCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
