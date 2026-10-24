using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Connector.Operation.Parameter;

/// <summary>
/// <c>txc environment connector operation parameter</c> — the values a single
/// operation parameter can take.
/// </summary>
[CliCommand(
    Name = "parameter",
    Description = "Get the values of a connector operation's dynamic parameters.",
    Children = new[] { typeof(ConnectorOperationParameterGetCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectorOperationParameterCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
