using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// Checks that a connection works, calling the connector's test endpoint when it has one.
/// Usage: <c>txc environment connection test &lt;connection-id&gt; [--connector &lt;name&gt;]</c>
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "test",
    Description = "Tests a connection in the LIVE connected environment, calling the connector's test endpoint when it has one. Requires an active profile. Result: status is healthy, unhealthy or untested (an application credential cannot run the platform's test); exit code 1 is returned with the payload when unhealthy, 2 for invalid input or an unknown connection."
)]
public class ConnectionTestCliCommand : ConnectionCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionTestCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionService>();
        var outcome = await service.TestAsync(Profile, Connector, ConnectionId, CancellationToken.None).ConfigureAwait(false);

        var status = !outcome.Tested ? ConnectionOutcomeStatus.Untested
            : outcome.Healthy ? ConnectionOutcomeStatus.Healthy
            : ConnectionOutcomeStatus.Unhealthy;
        OutputFormatter.WriteData(
            new
            {
                status = status.ToKebabCase(),
                connectionId = ConnectionId,
                connectionStatus = outcome.ConnectionStatus,
                statusMessage = outcome.Message,
            },
            _ => OutputWriter.WriteLine(status switch
            {
                ConnectionOutcomeStatus.Healthy => $"Connection {ConnectionId} is healthy ({outcome.ConnectionStatus}).",
                ConnectionOutcomeStatus.Untested => $"Connection {ConnectionId} is {outcome.ConnectionStatus}, not tested: {outcome.Message}",
                _ => $"Connection {ConnectionId} is not healthy ({outcome.ConnectionStatus}): {outcome.Message}",
            }));

        if (outcome.Healthy)
            return ExitSuccess;

        Logger.LogError("Connection {ConnectionId} is not healthy: {Message}", ConnectionId, outcome.Message);
        return ExitError;
    }
}
