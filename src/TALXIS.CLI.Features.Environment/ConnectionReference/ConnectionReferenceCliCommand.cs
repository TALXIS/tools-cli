using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// <c>txc environment connection-reference</c> — the ALM-safe placeholder that
/// flows and apps bind to instead of a connection.
/// </summary>
[CliCommand(
    Name = "connection-reference",
    Description = "Manage connection references in the LIVE connected environment. A connection reference is a solution component with a stable logical name that points at a connection of a connector; flows bind to it so a solution never carries connection ids, which differ on every environment.",
    Children = new[]
    {
        typeof(ConnectionReferenceListCliCommand),
        typeof(ConnectionReferenceGetCliCommand),
        typeof(ConnectionReferenceCreateCliCommand),
        typeof(ConnectionReferenceUpdateCliCommand),
        typeof(ConnectionReferenceDeleteCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectionReferenceCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
