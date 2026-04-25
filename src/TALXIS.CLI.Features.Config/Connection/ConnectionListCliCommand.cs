using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Config.Connection;

/// <summary>
/// <c>txc config connection list</c> — JSON dump of all connections.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List connections as JSON."
)]
public class ConnectionListCliCommand : TxcLeafCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ConnectionListCliCommand));
    protected override ILogger Logger => _logger;

    protected override async Task<int> ExecuteAsync()
    {
        var store = TxcServices.Get<IConnectionStore>();
        var connections = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(connections);
        return ExitSuccess;
    }
}
