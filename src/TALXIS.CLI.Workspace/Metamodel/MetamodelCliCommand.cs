// Reserved skeleton — intentionally NOT wired into any parent's `Children` array.
//
// Group parent for the future `txc workspace metamodel` family. `metamodel describe`
// and `metamodel list` will expose the metamodel that describes the shape of
// user-authored workspace artifacts — the grammar driving validation, the
// language server, and richer scaffolding beyond the current templating-
// engine path.
//
// Because it is unreachable from `WorkspaceCliCommand.Children`, DotMake will
// not surface it or its children to the CLI and the MCP adapter will not
// surface them as tools. Activating the group is a two-edit change:
//   1. Add `typeof(MetamodelCliCommand)` to `WorkspaceCliCommand.Children`.
//   2. Replace the `NotImplementedException` bodies under Metamodel/ with real
//      implementations.
//
// Please read `CONTRIBUTING.md` before changing the shape of this class.

using DotMake.CommandLine;

namespace TALXIS.CLI.Workspace.Metamodel;

[CliCommand(
    Description = "Reserved — not yet implemented.",
    Name = "metamodel",
    Children = new[]
    {
        typeof(MetamodelDescribeCliCommand),
        typeof(MetamodelListCliCommand)
    })]
public sealed class MetamodelCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
