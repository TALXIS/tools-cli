using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component.Dependency;

[CliCommand(
    Name = "dependency",
    Alias = "dep",
    Description = "Analyze component dependencies.",
    Children = new[]
    {
        typeof(ComponentDependencyListCliCommand),
        typeof(ComponentDependencyRequiredCliCommand),
        typeof(ComponentDependencyDeleteCheckCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ComponentDependencyCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
