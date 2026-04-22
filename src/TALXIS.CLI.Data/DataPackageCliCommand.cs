using DotMake.CommandLine;

namespace TALXIS.CLI.Data;

[CliCommand(
    Name = "package",
    Alias = "pkg",
    Description = "Configuration migration tool (CMT) for moving data between different environments",
    Children = new[] { typeof(DataPackageConvertCliCommand), typeof(DataPackageImportCliCommand) }
)]
public class DataPackageCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
