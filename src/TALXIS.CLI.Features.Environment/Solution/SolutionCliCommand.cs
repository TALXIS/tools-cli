using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliCommand(
    Name = "solution",
    Alias = "sln",
    Description = "Manage solutions in the target environment.",
    Children = new[] { typeof(SolutionImportCliCommand), typeof(SolutionUninstallCliCommand), typeof(SolutionListCliCommand) }
)]
public class SolutionCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
