using DotMake.CommandLine;

namespace TALXIS.CLI;

[CliCommand(
    Description = "Tool for automating development loops in Power Platform",
    Children = new[] { typeof(Data.DataCliCommand), typeof(Docs.DocsCliCommand), typeof(Workspace.WorkspaceCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TxcCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
