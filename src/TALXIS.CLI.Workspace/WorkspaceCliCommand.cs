using DotMake.CommandLine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Develop solutions in your local workspace",
    Alias = "ws",
    Children = new[]
    {
        typeof(ComponentCliCommand)
    })]
public class WorkspaceCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
