// Reserved skeleton — intentionally NOT wired into any parent's `Children` array.
//
// `txc workspace metamodel describe` will render a human-readable description
// of a named metamodel entity (component kind, relationship, validation rule...)
// that governs user-authored workspace artifacts.
//
// See MetamodelCliCommand.cs and CONTRIBUTING.md for the activation procedure
// and the design philosophy that governs this surface.

using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace.Metamodel;

[CliCommand(
    Description = "Reserved — not yet implemented.",
    Name = "describe")]
public sealed class MetamodelDescribeCliCommand
{
    public Task<int> RunAsync()
    {
        throw new NotImplementedException(
            "`workspace metamodel describe` is reserved for a future metamodel "
            + "inspection workflow and is not yet implemented. See CONTRIBUTING.md.");
    }
}
