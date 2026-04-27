using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Data;

[CliCommand(
    Name = "package",
    Alias = "pkg",
    Description = "Configuration migration tool (CMT) for moving data between different environments",
    Children = new[] { typeof(DataPackageImportCliCommand), typeof(DataPackageExportCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class DataPackageCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
