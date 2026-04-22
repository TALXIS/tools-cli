using DotMake.CommandLine;

namespace TALXIS.CLI.Environment.Deployment;

[CliCommand(
    Name = "deployment",
    Alias = "deploy",
    Description = "Inspect past package and solution deployment runs in the target environment.",
    Children = new[] { typeof(DeploymentListCliCommand), typeof(DeploymentShowCliCommand) }
)]
public class DeploymentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
