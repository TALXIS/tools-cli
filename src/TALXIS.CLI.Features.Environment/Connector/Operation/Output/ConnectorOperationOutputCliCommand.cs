using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connector.Operation.Output;

/// <summary>
/// <c>txc environment connector operation output</c> — the fields of a
/// connector operation's dynamic outputs.
/// </summary>
[CliCommand(
    Name = "output",
    Description = "Get the fields of a connector operation's dynamic outputs and the expressions that read them.",
    Children = new[] { typeof(ConnectorOperationOutputGetCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectorOperationOutputCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
