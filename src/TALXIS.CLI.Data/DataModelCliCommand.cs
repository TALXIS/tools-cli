using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

[CliCommand(
    Name = "model",
    Description = "Data modeling utilities"
)]
public class DataModelCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
