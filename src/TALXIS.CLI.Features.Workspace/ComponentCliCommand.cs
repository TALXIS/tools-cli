using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace;

/// <summary>
/// Workspace component operations — create and inspect components in the local repository.
/// Component type introspection (list, explain) is at the top level: <c>txc component type list/explain</c>.
/// </summary>
[CliCommand(
    Description = "Create and inspect components in your local workspace",
    Name = "component",
    Alias = "comp",
    Children = new[]
    {
        typeof(ComponentCreateCliCommand),
        typeof(ComponentParameterListCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
