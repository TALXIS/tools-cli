using DotMake.CommandLine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Create or modify components of your solution",
    Name = "component",
    Alias = "c",
    Children = new[]
    {
        typeof(ComponentListCliCommand),
        typeof(ComponentCreateCliCommand),
        typeof(ComponentExplainCliCommand)
    })]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }

    [CliCommand(
        Description = "Parameters for a specific component",
        Children = new[] { typeof(ComponentParameterListCliCommand) },
        Name = "parameter")]
    public class ComponentParameterCliCommand
    {
        public void Run(CliContext context)
        {
            context.ShowHelp();
        }
    }
}
