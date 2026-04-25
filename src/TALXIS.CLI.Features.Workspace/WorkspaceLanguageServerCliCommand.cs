#pragma warning disable TXC001 // Reserved stub — will inherit TxcLeafCommand when implemented
#pragma warning disable TXC004 // Reserved stub — safety annotation deferred until implemented
// Reserved skeleton — intentionally NOT wired into any parent's `Children` array.
//
// This class exists to pin the design: `txc workspace language-server` is the
// planned slot for a VS Code language server hosted in this CLI. It will drive
// editor features (completion, diagnostics, navigation) off the same object
// model that future `workspace validate` and `workspace model` commands use.
//
// Because it is unreachable from `WorkspaceCliCommand.Children`, DotMake will
// not surface it to the CLI and the MCP adapter will not surface it as a tool.
// Activating this command is a two-edit change:
//   1. Add `typeof(WorkspaceLanguageServerCliCommand)` to the parent's `Children` array.
//   2. Replace the `NotImplementedException` body with a real implementation.
//
// Please read `CONTRIBUTING.md` before changing the shape of this class.

using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace;

[CliCommand(
    Description = "Reserved — not yet implemented.",
    Name = "language-server")]
public sealed class WorkspaceLanguageServerCliCommand
{
    public Task<int> RunAsync()
    {
        throw new NotImplementedException(
            "`workspace language-server` is reserved for a future VS Code language "
            + "server host and is not yet implemented. See CONTRIBUTING.md.");
    }
}
