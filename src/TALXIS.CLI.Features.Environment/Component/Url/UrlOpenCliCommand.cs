using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Url;

/// <summary>
/// Builds a component URL, returns it, and opens it in the default browser.
/// In headless mode, only prints the URL without opening the browser.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "open",
    Description = "Build a component URL, return it, and open it in the default browser. Requires an active profile.",
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UrlOpenCliCommand : UrlCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<UrlOpenCliCommand>();

    protected override async Task<int> ExecuteAsync()
    {
        var result = await BuildUrlFromOptionsAsync().ConfigureAwait(false);
        if (result is null)
            return ExitValidationError;

#pragma warning disable TXC003 // OutputWriter used inside OutputFormatter textRenderer callback
        OutputFormatter.WriteData(
            new { url = result.Url.AbsoluteUri, type = result.TypeName },
            _ => OutputWriter.WriteLine(result.Url.AbsoluteUri));
#pragma warning restore TXC003

        BrowserLauncher.Open(result.Url, Logger);
        return ExitSuccess;
    }
}
