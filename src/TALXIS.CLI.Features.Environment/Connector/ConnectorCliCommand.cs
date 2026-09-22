using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// <c>txc environment connector</c> — browse Power Automate connector metadata
/// in the connected environment while authoring a flow locally.
/// </summary>
[CliCommand(
    Name = "connector",
    Description = "Browse Power Automate connectors and their operations in the connected environment.",
    Children = new[]
    {
        typeof(ConnectorListCliCommand),
        typeof(ConnectorGetCliCommand),
        typeof(Operation.ConnectorOperationCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectorCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
