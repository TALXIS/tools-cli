using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Lists Power Automate connectors available in the LIVE connected environment. Requires an active profile. Use 'connector get' to see a connector's operations."
)]
public class ConnectorListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only connectors whose name or display name contains this text.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectorMetadataService>();
        var connectors = await service.ListConnectorsAsync(Profile, Filter, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteList(connectors, PrintTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectorSummary> connectors)
    {
        if (connectors.Count == 0) { OutputWriter.WriteLine("No connectors found."); return; }

        var nameWidth = Math.Max(connectors.Max(c => c.LogicalName.Length), 20);
        var header = $"{"Name".PadRight(nameWidth)} | {"Tier".PadRight(10)} | Display name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var c in connectors)
            OutputWriter.WriteLine($"{c.LogicalName.PadRight(nameWidth)} | {(c.Tier ?? "-").PadRight(10)} | {c.DisplayName}");
    }
#pragma warning restore TXC003
}
