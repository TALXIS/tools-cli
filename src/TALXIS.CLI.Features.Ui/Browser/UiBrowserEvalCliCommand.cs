using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Browser;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Ui.Browser;

[CliReadOnly]
[CliCommand(Name = "eval", Description = "Evaluate JavaScript in the active browser session.")]
public class UiBrowserEvalCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UiBrowserEvalCliCommand));

    [CliOption(Name = "--eval", Aliases = ["-e"], Description = "JavaScript expression to evaluate.", Required = true)]
    public string Eval { get; set; } = string.Empty;

    [CliOption(Name = "--session", Aliases = ["-s"], Description = "Session ID to use. Defaults to the active session.", Required = false)]
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

        var result = await sessionManager.EvaluateAsync(session.Id, Eval, CancellationToken.None).ConfigureAwait(false);
        var raw = result.ValueKind == JsonValueKind.String
            ? JsonSerializer.Serialize(result.GetString(), TxcOutputJsonOptions.Default)
            : result.GetRawText();

        OutputFormatter.WriteRaw(raw, () =>
        {
            if (result.ValueKind == JsonValueKind.String)
                OutputWriter.WriteLine(result.GetString() ?? string.Empty);
            else
                OutputWriter.WriteLine(result.GetRawText());
        });

        return ExitSuccess;
    }
}
