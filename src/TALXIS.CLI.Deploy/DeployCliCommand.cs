using DotMake.CommandLine;

namespace TALXIS.CLI.Deploy;

[CliCommand(
    Name = "deploy",
    Description = "Deploy Power Platform packages, solutions, and inspect deployment logs against Dataverse environments.",
    Children = new[] { typeof(DeployRunCliCommand), typeof(DeployListCliCommand), typeof(DeployShowCliCommand), typeof(DeployUninstallCliCommand) }
)]
public class DeployCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
