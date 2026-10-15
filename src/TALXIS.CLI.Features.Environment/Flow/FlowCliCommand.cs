using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Flow;

/// <summary>
/// <c>txc environment flow</c> — work with locally authored cloud flow
/// definitions against the connected environment.
/// </summary>
[CliCommand(
    Name = "flow",
    Description = "Check locally authored cloud flow definitions against the connected environment.",
    Children = new[] { typeof(FlowValidateCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class FlowCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
