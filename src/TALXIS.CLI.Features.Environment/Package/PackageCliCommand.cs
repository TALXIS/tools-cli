using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Package;

[CliCommand(
    Name = "package",
    Alias = "pkg",
    Description = "Manage deployable packages in the target environment.",
    Children = new[] { typeof(PackageImportCliCommand), typeof(PackageUninstallCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class PackageCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
