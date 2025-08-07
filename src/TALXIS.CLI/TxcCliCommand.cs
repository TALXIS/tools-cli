using DotMake.CommandLine;

namespace TALXIS.CLI;

[CliCommand(
    Children = new[] { typeof(Data.DataCliCommand), typeof(Docs.DocsCliCommand), typeof(DataVisualizer.DataVisualizer), typeof(Workspace.WorkspaceCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TxcCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
