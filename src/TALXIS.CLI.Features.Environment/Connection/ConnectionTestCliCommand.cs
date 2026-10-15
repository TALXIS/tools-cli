using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// <c>txc environment connection test</c> — checks that a connection works,
/// calling the connector's test endpoint when it has one.
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "test",
    Description = "Tests a connection in the LIVE connected environment. Requires an active profile. Exits 1 when the connection is not healthy."
)]
public class ConnectionTestCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionTestCliCommand));

    [CliArgument(Name = "connection-id", Description = "Connection id, exactly as shown by 'environment connection list'.")]
    public string ConnectionId { get; set; } = null!;

    [CliOption(Name = "--connector", Description = "Connector of the connection (logical name, apiId or exact display name).", Required = true)]
    public string Connector { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionService>();
        var outcome = await service.TestAsync(Profile, Connector, ConnectionId, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new
            {
                connectionId = ConnectionId,
                healthy = outcome.Healthy,
                connectionStatus = outcome.ConnectionStatus,
                message = outcome.Message,
            },
            _ =>
            {
#pragma warning disable TXC003
                OutputWriter.WriteLine(outcome.Healthy
                    ? $"Connection {ConnectionId} is healthy ({outcome.ConnectionStatus})."
                    : $"Connection {ConnectionId} is not healthy ({outcome.ConnectionStatus}): {outcome.Message}");
#pragma warning restore TXC003
            });

        if (outcome.Healthy)
            return ExitSuccess;

        Logger.LogError("Connection {ConnectionId} is not healthy: {Message}", ConnectionId, outcome.Message);
        return ExitError;
    }
}
