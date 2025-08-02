using DotMake.CommandLine;

namespace TALXIS.CLI;

[CliCommand(
    Description = "Tool for automating development loops in Power Platform.",
    Children = new[] { typeof(TALXIS.CLI.Data.DataCliCommand), typeof(TALXIS.CLI.Workspace.WorkspaceCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TxcCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
