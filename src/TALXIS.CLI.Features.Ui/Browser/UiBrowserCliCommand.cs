using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Ui.Browser;

[CliCommand(
    Name = "browser",
    Description = "Advanced browser escape hatches for an open session.",
    Children = new[] { typeof(UiBrowserEvalCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UiBrowserCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
