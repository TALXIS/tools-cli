using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>
/// <c>txc environment connector operation</c> — find connector operations and
/// read their exact parameter specifications.
/// </summary>
[CliCommand(
    Name = "operation",
    Description = "Find connector operations, their parameters and outputs, and the values of dynamic ones.",
    Children = new[]
    {
        typeof(ConnectorOperationSearchCliCommand),
        typeof(ConnectorOperationGetCliCommand),
        typeof(Parameter.ConnectorOperationParameterCliCommand),
        typeof(Output.ConnectorOperationOutputCliCommand),
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
