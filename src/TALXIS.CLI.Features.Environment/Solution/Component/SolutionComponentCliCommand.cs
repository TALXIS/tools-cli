using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Solution.Component;

[CliCommand(
    Name = "component",
    Description = "Manage components within a solution.",
    Children = new[] { typeof(SolutionComponentListCliCommand), typeof(SolutionComponentCountCliCommand), typeof(SolutionComponentAddCliCommand), typeof(SolutionComponentRemoveCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class SolutionComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
