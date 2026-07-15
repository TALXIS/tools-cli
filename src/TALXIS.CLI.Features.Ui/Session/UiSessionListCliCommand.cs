using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Browser;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Ui.Session;

[CliReadOnly]
[CliCommand(Name = "list", Description = "List active browser sessions.")]
public class UiSessionListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UiSessionListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var sessionManager = TxcServices.Get<IBrowserSessionManager>();
        var sessions = await sessionManager.ListSessionsAsync(CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(sessions, values =>
        {
            foreach (var value in values)
                OutputWriter.WriteLine($"{value.Id}  {value.ProfileName}  {value.AppUrl}");
        });

        return ExitSuccess;
    }
}
