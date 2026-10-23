using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

/// <summary>
/// <c>txc environment connection-reference</c> — the connection references a
/// solution-aware cloud flow binds its connector actions to.
/// </summary>
[CliCommand(
    Name = "connection-reference",
    Description = "Inspect connection references in the connected environment.",
    Children = new[] { typeof(ConnectionReferenceListCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class ConnectionReferenceCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
