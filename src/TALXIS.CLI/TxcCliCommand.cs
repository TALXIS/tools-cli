using DotMake.CommandLine;

namespace TALXIS.CLI;

[CliCommand(
    Description = "TALXIS CLI (txc): Tool for automating development loops in Power Platform.",
    Children = new[] { typeof(TALXIS.CLI.Data.DataCliCommand), typeof(TALXIS.CLI.Component.ComponentCliCommand) }
)]
public class TxcCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
