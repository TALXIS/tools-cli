using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Deployment;

[CliCommand(
    Name = "deployment",
    Alias = "deploy",
    Description = "Inspect past package and solution deployment runs in the target environment.",
    Children = new[] { typeof(DeploymentListCliCommand), typeof(DeploymentGetCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class DeploymentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
