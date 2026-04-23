using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment;

[CliCommand(
    Name = "environment",
    Alias = "env",
    Description = "Manage the footprint of your project in a live target environment (packages, solutions, deployment history).",
    Children = new[] { typeof(Package.PackageCliCommand), typeof(Solution.SolutionCliCommand), typeof(Deployment.DeploymentCliCommand) }
)]
public class EnvironmentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
