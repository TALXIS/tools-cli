using DotMake.CommandLine;

namespace TALXIS.CLI.Environment;

[CliCommand(
    Name = "environment",
    Description = "Environment deployment utilities for Dataverse packages",
    Children = new[] { typeof(EnvironmentInstallCliCommand) }
)]
public class EnvironmentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
