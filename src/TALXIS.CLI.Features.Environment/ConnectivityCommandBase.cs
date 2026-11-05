using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Environment.Connection;

namespace TALXIS.CLI.Features.Environment;

/// <summary>
/// Shared base of the connector, connection and connection-reference commands. An
/// unknown connector or connection ends the command with a validation error (exit 2)
/// that also names the command to run next.
/// </summary>
public abstract class ConnectivityCommandBase : ProfiledCliCommand
{
    protected override string? GetValidationHint(Exception ex) => ConnectionCommandSupport.HintFor(ex);
}
