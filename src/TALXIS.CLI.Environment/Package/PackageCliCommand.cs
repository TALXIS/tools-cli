using DotMake.CommandLine;

namespace TALXIS.CLI.Environment.Package;

[CliCommand(
    Name = "package",
    Alias = "pkg",
    Description = "Manage deployable packages in the target environment.",
    Children = new[] { typeof(PackageImportCliCommand), typeof(PackageUninstallCliCommand) }
)]
public class PackageCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
