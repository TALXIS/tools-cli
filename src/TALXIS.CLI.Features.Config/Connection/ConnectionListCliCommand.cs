using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.Features.Config.Connection;

/// <summary>
/// <c>txc config connection list</c> — JSON dump of all connections.
/// </summary>
[CliCommand(
    Name = "list",
    Description = "List connections as JSON."
)]
public class ConnectionListCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(ConnectionListCliCommand));

    public async Task<int> RunAsync()
    {
        try
        {
            var store = TxcServices.Get<IConnectionStore>();
            var connections = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
            OutputWriter.WriteLine(JsonSerializer.Serialize(connections, TxcJsonOptions.Default));
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list connections.");
            return 1;
        }
    }
}
