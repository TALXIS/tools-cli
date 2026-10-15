using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connection.Permission;

/// <summary>
/// <c>txc environment connection permission</c> — who can use a connection.
/// Sharing a connection lets another identity (typically a deploy service
/// principal) bind connection references to it.
/// </summary>
[CliCommand(
    Name = "permission",
    Description = "Manage who can use a connection (share it) in the LIVE connected environment.",
    Children = new[]
    {
        typeof(ConnectionPermissionListCliCommand),
        typeof(ConnectionPermissionAddCliCommand),
        typeof(ConnectionPermissionRemoveCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectionPermissionCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
