using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

[CliCommand(
    Name = "operation",
    Description = "Inspect the operations (actions and triggers) a connector exposes.",
    Children = new[]
    {
        typeof(ConnectorOperationListCliCommand),
        typeof(ConnectorOperationGetCliCommand),
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectorOperationCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
