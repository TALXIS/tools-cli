using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliCommand(
    Name = "solution",
    Alias = "sln",
    Description = "Manage solutions in the target environment.",
    Children = new[] { typeof(SolutionImportCliCommand), typeof(SolutionUninstallCliCommand), typeof(SolutionDeleteCliCommand), typeof(SolutionListCliCommand), typeof(SolutionShowCliCommand), typeof(SolutionCreateCliCommand), typeof(SolutionPublishCliCommand), typeof(SolutionUninstallCheckCliCommand), typeof(Component.SolutionComponentCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class SolutionCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
