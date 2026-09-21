using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// <c>txc environment connector list</c> — the connector catalog of the
/// connected environment.
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "list",
    Description = "Lists Power Automate connectors available in the LIVE connected environment. Requires an active profile. Use 'connector get' to see a connector's operations."
)]
public class ConnectorListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorListCliCommand));

    [CliOption(Name = "--query", Description = "Show only connectors whose name or display name contains this text.", Required = false)]
    public string? Query { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of connectors to request from the environment.", Required = false)]
    public int? Top { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPowerAutomateConnectorService>();
        var connectors = await service.ListConnectorsAsync(Profile, Query, Top, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteList(connectors, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectorSummary> connectors)
    {
        if (connectors.Count == 0)
        {
            OutputWriter.WriteLine("No connectors found.");
            return;
        }

        var nameWidth = Math.Clamp(connectors.Max(c => c.Name.Length), 20, 50);
        var header = $"{"Name".PadRight(nameWidth)} | {"Tier".PadRight(12)} | Display name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var connector in connectors)
        {
            var name = Fit(connector.Name, nameWidth);
            var tier = Fit(connector.Tier ?? "-", 12);
            OutputWriter.WriteLine($"{name.PadRight(nameWidth)} | {tier.PadRight(12)} | {connector.DisplayName}");
        }
    }

    private static string Fit(string value, int width)
        => value.Length > width ? value[..(width - 1)] + "." : value;
#pragma warning restore TXC003
}
