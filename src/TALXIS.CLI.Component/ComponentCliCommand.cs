using DotMake.CommandLine;
namespace TALXIS.CLI.Component;

[CliCommand(
    Description = "Create or modify components of your solution"
)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }

    [CliCommand(Description = "Use component templates to scaffold components",
    Children = new[] {
        typeof(TemplateListCliCommand),
        typeof(TemplateScaffoldCliCommand)
    })]
    public class TemplateCliCommand
    {
        public void Run(CliContext context)
        {
            context.ShowHelp();
        }
        [CliCommand(Description = "Parameters for a specific component template",
        Children = new[] { typeof(TemplateParameterListCliCommand) },
        Name = "parameter")]
        public class TemplateParameterCliCommand
        {
            public void Run(CliContext context)
            {
                context.ShowHelp();
            }
        }
    }
}
