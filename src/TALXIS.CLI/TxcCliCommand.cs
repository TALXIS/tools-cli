using DotMake.CommandLine;

namespace TALXIS.CLI;

[CliCommand(
    Description = "Tool for automating development loops in Power Platform",
    Children = new[] { typeof(Data.DataCliCommand), typeof(Environment.EnvironmentCliCommand), typeof(Workspace.WorkspaceCliCommand), typeof(TALXIS.CLI.Config.Commands.ConfigCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TxcCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
