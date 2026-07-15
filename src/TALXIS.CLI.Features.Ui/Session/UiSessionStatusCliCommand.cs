using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Browser;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Ui.Session;

[CliReadOnly]
[CliCommand(Name = "status", Description = "Show the current browser session.")]
public class UiSessionStatusCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UiSessionStatusCliCommand));

    [CliOption(Name = "--session", Aliases = ["-s"], Description = "Session ID to inspect. Defaults to the active session.", Required = false)]
    public string? Session { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var sessionManager = TxcServices.Get<IBrowserSessionManager>();
        var session = !string.IsNullOrWhiteSpace(Session)
            ? await sessionManager.GetSessionAsync(Session, CancellationToken.None).ConfigureAwait(false)
            : await sessionManager.GetActiveSessionAsync(CancellationToken.None).ConfigureAwait(false);

        if (session is null)
        {
            Logger.LogError("No active session. Run 'txc ui session open' first.");
            return ExitValidationError;
        }

        var currentUrl = await sessionManager.GetCurrentUrlAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
        var current = string.IsNullOrWhiteSpace(currentUrl) ? session : session with { AppUrl = currentUrl };

        OutputFormatter.WriteData(current, value =>
        {
            OutputWriter.WriteLine($"Session {value.Id}");
            OutputWriter.WriteLine($"  Profile: {value.ProfileName}");
            OutputWriter.WriteLine($"  URL: {value.AppUrl}");
            OutputWriter.WriteLine($"  PID: {value.Pid}");
            OutputWriter.WriteLine($"  Headless: {value.Headless}");
        });

        return ExitSuccess;
    }
}
