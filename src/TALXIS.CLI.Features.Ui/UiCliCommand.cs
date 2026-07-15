using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Ui;

[CliCommand(
    Name = "ui",
    Description = "Browser automation for Power Apps model-driven apps.",
    Children = new[] { typeof(Session.UiSessionCliCommand), typeof(Browser.UiBrowserCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UiCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
