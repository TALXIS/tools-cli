using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

[CliCommand(
    Description = "Data-related utilities for ETL, Power Query and migration scenarios",
    Children = new[] { typeof(TransformServerCliCommand), typeof(ConvertDataCliCommand) }
)]
public class DataCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
