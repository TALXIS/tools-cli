using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>
/// <c>txc environment connector operation</c> — find connector operations and
/// read their exact parameter specifications.
/// </summary>
[CliCommand(
    Name = "operation",
    Description = "Search connector operations and read their exact parameter specifications.",
    Children = new[]
    {
        typeof(ConnectorOperationSearchCliCommand),
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
