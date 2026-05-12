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
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }

    /// <summary>Scaffolding parameter introspection for component creation.</summary>
    [CliCommand(
        Description = "Scaffolding parameters for component creation",
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
}
