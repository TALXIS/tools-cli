using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Data;

[CliCommand(
    Description = "Offline data utilities for modeling, demonstration, migration and integration",
    Children = new[] { typeof(TransformCliCommand), typeof(DataPackageCliCommand), typeof(DataModelCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class DataCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
