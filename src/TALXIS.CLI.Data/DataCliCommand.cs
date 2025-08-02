using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

[CliCommand(
    Description = "Data utilities for modeling, demostration, migration and integration",
    Children = new[] { typeof(TransformCliCommand), typeof(DataPackageCliCommand), typeof(DataModelCliCommand) }
)]
public class DataCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
