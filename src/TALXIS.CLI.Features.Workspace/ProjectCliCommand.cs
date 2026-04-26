using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace;

[CliCommand(
    Description = "Work with MSBuild projects in your workspace (solutions, plugins, libraries, controls...)",
    Alias = "proj",
    Children = new[]
    {
        typeof(ProjectExplainCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ProjectCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
