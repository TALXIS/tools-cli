// Reserved skeleton — intentionally NOT wired into any parent's `Children` array.
//
// This class exists to pin the design: `txc workspace validate` is the planned
// slot for object-model-driven validation of the local workspace (schema,
// references, metadata consistency) — the next step beyond template-engine
// scaffolding.
//
// Because it is unreachable from `WorkspaceCliCommand.Children`, DotMake will
// not surface it to the CLI and the MCP adapter will not surface it as a tool.
// Activating this command is a two-edit change:
//   1. Add `typeof(WorkspaceValidateCliCommand)` to the parent's `Children` array.
//   2. Replace the `NotImplementedException` body with a real implementation.
//
// Please read `CONTRIBUTING.md` before changing the shape of this class.

using DotMake.CommandLine;

namespace TALXIS.CLI.Workspace;

[CliCommand(
    Description = "Reserved — not yet implemented.",
    Name = "validate")]
public sealed class WorkspaceValidateCliCommand
{
    public Task<int> RunAsync()
    {
        throw new NotImplementedException(
            "`workspace validate` is reserved for a future object-model-driven "
            + "validation workflow and is not yet implemented. See CONTRIBUTING.md.");
    }
}
