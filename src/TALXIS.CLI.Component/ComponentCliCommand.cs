using DotMake.CommandLine;
namespace TALXIS.CLI.Component;

[CliCommand(
    Description = "Create or modify components of your solution",
    Children = new[] {
        typeof(TemplateListCliCommand),
        typeof(TemplateParameterListCliCommand),
        typeof(TemplateScaffoldCliCommand)
    }
)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHierarchy();
    }
}
