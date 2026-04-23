// Reserved skeleton — intentionally NOT wired into any parent's `Children` array.
//
// This class exists to pin the design: `txc environment patch` is the planned
// slot for a future git-diff-driven incremental push that computes the minimal
// set of changes since a reference (commit, tag, last deploy) and applies only
// those to the target environment.
//
// Because it is unreachable from `TxcCliCommand.Children`, DotMake will not
// surface it to the CLI and the MCP adapter will not surface it as a tool.
// Activating this command is a two-edit change:
//   1. Add `typeof(DeploymentPatchCliCommand)` to the parent's `Children` array.
//   2. Replace the `NotImplementedException` body with a real implementation.
//
// Please read `CONTRIBUTING.md` before changing the shape of this class —
// the command surface follows a deliberate nouns-over-platforms philosophy.

using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Deployment;

[CliCommand(
    Description = "Reserved — not yet implemented.",
    Name = "patch")]
public sealed class DeploymentPatchCliCommand
{
    public Task<int> RunAsync()
    {
        throw new NotImplementedException(
            "`environment deployment patch` is reserved for a future incremental-push "
            + "workflow and is not yet implemented. See CONTRIBUTING.md.");
    }
}
