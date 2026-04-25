using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace;

[CliCommand(
    Description = "Work with MSBuild projects in your workspace (solutions, plugins, libraries, controls...)",
    Alias = "p",
    Children = new[]
    {
        typeof(ProjectExplainCliCommand)
    }
)]
public class ProjectCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
