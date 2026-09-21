using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// <c>txc environment connection</c> — the connections and connection
/// references a flow can bind to.
/// </summary>
[CliCommand(
    Name = "connection",
    Description = "Inspect connections and connection references available to flows.",
    Children = new[] { typeof(ConnectionListCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectionCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
