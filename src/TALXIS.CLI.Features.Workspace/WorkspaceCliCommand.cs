using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace;

[CliCommand(
    Description = "Implement software in your local computer workspace (Git repository)",
    Alias = "ws",
    Children = new[]
    {
        typeof(ComponentCliCommand),
        typeof(ProjectCliCommand),
        typeof(WorkspaceExplainCliCommand),
        typeof(WorkspaceValidateCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None)]
public class WorkspaceCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
