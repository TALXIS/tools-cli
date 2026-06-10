using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Plugin.Types;

[CliCommand(
    Name = "type",
    Description = "List plugin types (plugins and workflow activities) registered in the connected environment.",
    Children = new[] { typeof(PluginTypeListCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PluginTypeCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
