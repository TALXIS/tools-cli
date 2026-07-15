using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Ui.Session;

[CliCommand(
    Name = "session",
    Description = "Manage browser sessions.",
    Children = new[] {
        typeof(UiSessionOpenCliCommand),
        typeof(UiSessionCloseCliCommand),
        typeof(UiSessionStatusCliCommand),
        typeof(UiSessionListCliCommand)
    },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UiSessionCliCommand
{
    public void Run(CliContext context) => context.ShowHelp();
}
