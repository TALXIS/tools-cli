using DotMake.CommandLine;

namespace TALXIS.CLI.Component;

[CliCommand(
    Description = "Create or modify components of your solution",
    Children = new[]
    {
        typeof(ComponentListCliCommand),
        typeof(ComponentCreateCliCommand)
    })]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }

    [CliCommand(
        Description = "Parameters for a specific component template",
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
