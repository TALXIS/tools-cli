using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// Lists the connections the caller owns or can use. Connection references are a separate concept.
/// Usage: <c>txc environment connection list [--connector &lt;name&gt;] [--filter &lt;text&gt;]</c>
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "list",
    Description = "Lists the connections you own or can use in the LIVE connected environment. Requires an active profile. A connection created by another identity only appears once it is shared with you."
)]
public class ConnectionListCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionListCliCommand));

    [CliOption(Name = "--connector", Description = "Show only connections of this connector (logical name, apiId or exact display name).", Required = false)]
    public string? Connector { get; set; }

    [CliOption(Name = "--filter", Description = "Show only connections whose display name contains this text (case-insensitive).", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionService>();
        var connections = await service.ListAsync(Profile, Connector, Filter, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(connections, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectionSummary> connections)
    {
        if (connections.Count == 0)
        {
            OutputWriter.WriteLine("No connections found.");
            return;
        }

        var header = $"{"Display name".PadRight(30)} | {"Connector".PadRight(34)} | {"Status".PadRight(14)} | Connection id";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var c in connections)
        {
            OutputWriter.WriteLine(
                $"{TableText.Truncate(c.DisplayName ?? "-", 30).PadRight(30)} | " +
                $"{TableText.Truncate(c.ApiId, 34).PadRight(34)} | " +
                $"{TableText.Truncate(c.ConnectionStatus ?? "-", 14).PadRight(14)} | {c.ConnectionId}");
        }
    }
#pragma warning restore TXC003
}
