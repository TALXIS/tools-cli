// Reserved skeleton — intentionally NOT wired into any parent's `Children` array.
//
// `txc workspace metamodel list` will enumerate metamodel entities (component
// kinds, relationship types, validation rules...) that govern user-authored
// workspace artifacts, with lightweight filtering.
//
// See MetamodelCliCommand.cs and CONTRIBUTING.md for the activation procedure
// and the design philosophy that governs this surface.

using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Workspace.Metamodel;

[CliCommand(
    Description = "Reserved — not yet implemented.",
    Name = "list")]
public sealed class MetamodelListCliCommand
{
    public Task<int> RunAsync()
    {
        throw new NotImplementedException(
            "`workspace metamodel list` is reserved for a future metamodel "
            + "enumeration workflow and is not yet implemented. See CONTRIBUTING.md.");
    }
}
