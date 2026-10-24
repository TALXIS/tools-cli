using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connector;

[CliCommand(
    Name = "connector",
    Description = "Discover connectors and their operations in the live environment (for authoring cloud flows).",
    Children = new[]
    {
        typeof(ConnectorListCliCommand),
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
