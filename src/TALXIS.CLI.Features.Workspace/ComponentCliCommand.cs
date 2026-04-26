using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace;

[CliCommand(
    Description = "Create or modify components of your solution",
    Name = "component",
    Alias = "comp",
    Children = new[]
    {
        typeof(ComponentCreateCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }

    [CliCommand(
        Description = "Parameters for a specific component",
        Children = new[] { typeof(ComponentParameterListCliCommand) },
        Name = "parameter",
        ShortFormAutoGenerate = CliNameAutoGenerate.None)]
    public class ComponentParameterCliCommand
    {
        public void Run(CliContext context)
        {
            context.ShowHelp();
        }
    }

    [CliCommand(
        Description = "Types of available components",
        Children = new[] { typeof(ComponentTypeListCliCommand), typeof(ComponentTypeExplainCliCommand) },
        Name = "type",
        ShortFormAutoGenerate = CliNameAutoGenerate.None)]
    public class ComponentTypeCliCommand
    {
        public void Run(CliContext context)
        {
            context.ShowHelp();
        }
    }
}
