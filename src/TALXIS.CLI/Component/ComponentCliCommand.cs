using DotMake.CommandLine;

namespace TALXIS.CLI.Component;

/// <summary>
/// Top-level component group — metamodel introspection.
/// Platform knowledge about component types, independent of any workspace or environment.
/// </summary>
[CliCommand(
    Name = "component",
    Alias = "comp",
    Description = "Inspect component type definitions, aliases, and metadata.",
    Children = new[] { typeof(ComponentTypeCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
