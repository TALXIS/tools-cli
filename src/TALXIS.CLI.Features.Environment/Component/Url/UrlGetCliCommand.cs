using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Url;

/// <summary>
/// Builds and returns the URL for a component editor/viewer without opening the browser.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Build a component URL and return it without opening the browser. Requires an active profile.",
    ShortFormAutoGenerate = CliNameAutoGenerate.None
)]
public class UrlGetCliCommand : UrlCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<UrlGetCliCommand>();

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

        return ExitSuccess;
    }
}
