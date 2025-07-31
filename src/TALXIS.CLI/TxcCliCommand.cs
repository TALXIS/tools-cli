using DotMake.CommandLine;

namespace TALXIS.CLI;

[CliCommand(
    Description = "TALXIS CLI (txc): A modular .NET global tool for automating and managing Power Platform development workflows.",
    Children = new[] { typeof(TALXIS.CLI.Data.DataCliCommand) }
)]
public class TxcCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHierarchy();
    }
}
