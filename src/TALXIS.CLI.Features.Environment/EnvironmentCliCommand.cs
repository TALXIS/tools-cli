using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment;

[CliCommand(
    Name = "environment",
    Alias = "env",
    Description = "Manage the footprint of your project in a live target environment (packages, solutions, deployment history).",
    Children = new[] { typeof(Package.PackageCliCommand), typeof(Solution.SolutionCliCommand), typeof(Deployment.DeploymentCliCommand), typeof(Data.EnvDataCliCommand), typeof(Entity.EntityCliCommand), typeof(Setting.SettingCliCommand), typeof(Changeset.ChangesetCliCommand) }
)]
public class EnvironmentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
