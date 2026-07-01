using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Currency;

/// <summary>
/// <c>txc environment currency</c> - look up currencies enabled in the
/// environment for use with <c>user-settings set --currency</c>.
/// </summary>
[CliCommand(
    Name = "currency",
    Description = "Look up currencies enabled in the environment.",
    Children = new[] { typeof(CurrencyListCliCommand) },
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class CurrencyCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
