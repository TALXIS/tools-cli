using DotMake.CommandLine;

namespace TALXIS.CLI;

[CliCommand(
    Description = "Tool for automating development loops in Power Platform",
    Children = new[] { typeof(TALXIS.CLI.Features.Data.DataCliCommand), typeof(TALXIS.CLI.Features.Environment.EnvironmentCliCommand), typeof(TALXIS.CLI.Features.Workspace.WorkspaceCliCommand), typeof(TALXIS.CLI.Features.Config.ConfigCliCommand), typeof(TALXIS.CLI.Features.Docs.DocsCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class TxcCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
