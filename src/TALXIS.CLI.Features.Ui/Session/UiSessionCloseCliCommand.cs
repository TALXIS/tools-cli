using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Browser;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Ui.Session;

[CliIdempotent]
[CliCommand(Name = "close", Description = "Close one or more browser sessions.")]
public class UiSessionCloseCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UiSessionCloseCliCommand));

    [CliOption(Name = "--session", Aliases = ["-s"], Description = "Session ID to close. Defaults to the active session.", Required = false)]
    public string? Session { get; set; }

    [CliOption(Name = "--all", Description = "Close all active sessions.", Required = false)]
    public bool All { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var sessionManager = TxcServices.Get<IBrowserSessionManager>();

        if (All)
        {
            var sessions = await sessionManager.ListSessionsAsync(CancellationToken.None).ConfigureAwait(false);
            if (sessions.Count == 0)
            {
                Logger.LogError("No active session. Run 'txc ui session open' first.");
                return ExitValidationError;
            }

            foreach (var value in sessions)
                await sessionManager.CloseAsync(value.Id, CancellationToken.None).ConfigureAwait(false);

            OutputFormatter.WriteResult("succeeded", $"Closed {sessions.Count} session(s).");
            return ExitSuccess;
        }

        var session = !string.IsNullOrWhiteSpace(Session)
            ? await sessionManager.GetSessionAsync(Session, CancellationToken.None).ConfigureAwait(false)
            : await sessionManager.GetActiveSessionAsync(CancellationToken.None).ConfigureAwait(false);

        if (session is null)
        {
            Logger.LogError("No active session. Run 'txc ui session open' first.");
            return ExitValidationError;
        }

        await sessionManager.CloseAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteResult("succeeded", $"Session {session.Id} closed.", session.Id);
        return ExitSuccess;
    }
}
