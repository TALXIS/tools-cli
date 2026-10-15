using DotMake.CommandLine;
using TALXIS.CLI.Features.Environment.Connector;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// <c>txc environment connection-reference</c> — the ALM-safe placeholder that
/// flows and apps bind to instead of a connection.
/// </summary>
[CliCommand(
    Name = "connection-reference",
    Description = "Manage connection references (the stable placeholder flows bind to instead of a connection) in the LIVE connected environment. " + ConnectorDescriptions.Workflow,
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
