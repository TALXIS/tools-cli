using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>
/// <c>txc environment connection list</c> — connection references a
/// solution-aware flow can bind to, falling back to raw connections.
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "list",
    Description = "Lists connections and connection references available to flows in the LIVE connected environment. Requires an active profile. Solution-aware flows bind by connection reference logical name."
)]
public class ConnectionListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionListCliCommand));

    [CliOption(Name = "--connector", Description = "Show only connections for this connector, for example shared_teams.", Required = false)]
    public string? Connector { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPowerAutomateConnectorService>();
        var connections = await service.ListConnectionsAsync(Profile, Connector, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteList(connections, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<FlowConnectionSummary> connections)
    {
        if (connections.Count == 0)
        {
            OutputWriter.WriteLine("No connections or connection references found.");
            return;
        }

        var header = $"{"Logical name / connection".PadRight(42)} | {"Connector".PadRight(42)} | Source";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var connection in connections)
        {
            var binding = Fit(connection.ConnectionReferenceLogicalName ?? connection.ConnectionName, 42);
            var connector = Fit(connection.ConnectorId, 42);
            OutputWriter.WriteLine($"{binding.PadRight(42)} | {connector.PadRight(42)} | {connection.Source}");
        }
    }

    private static string Fit(string value, int width)
        => value.Length > width ? value[..(width - 1)] + "." : value;
#pragma warning restore TXC003
}
