using DotMake.CommandLine;
using TALXIS.CLI.Features.Environment.Connection.Permission;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// <c>txc environment connection</c> — the stored sign-in of a connector in the
/// live environment. A connection is per environment and is never exported with a
/// solution; solutions bind to a connection reference instead.
/// </summary>
[CliCommand(
    Name = "connection",
    Description = "Manage connections (the stored sign-in of a connector) in the LIVE connected environment. Not 'config connection', which stores local environment endpoints.",
    Children = new[]
    {
        typeof(ConnectionCreateCliCommand),
        typeof(ConnectionListCliCommand),
        typeof(ConnectionGetCliCommand),
        typeof(ConnectionTestCliCommand),
        typeof(ConnectionDeleteCliCommand),
        typeof(ConnectionPermissionCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectionCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
