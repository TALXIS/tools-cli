using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "search",
    Description = "Searches Power Automate operations across connectors in the LIVE connected environment, for when the connector is not yet known. Requires an active profile. Deprecated operations are excluded."
)]
public class ConnectorOperationSearchCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationSearchCliCommand));

    [CliArgument(Name = "search", Description = "Text to search for in operation names and descriptions, for example \"send email\".")]
    public string Search { get; set; } = null!;

    [CliOption(Name = "--connector", Description = "Limit the search to one connector. " + ConnectorDescriptions.Identifier, Required = false)]
    public string? Connector { get; set; }

    [CliOption(Name = "--kind", Description = "Which operations to return: all, action, or trigger.", Required = false)]
    public string Kind { get; set; } = "all";

    [CliOption(Name = "--top", Description = "Maximum number of results to return.", Required = false)]
    public int Top { get; set; } = 20;

    private static readonly string[] Kinds = ["all", "action", "trigger"];

    protected override async Task<int> ExecuteAsync()
    {
        var kind = Kind.Trim().ToLowerInvariant();
        if (!Kinds.Contains(kind))
        {
            Logger.LogError("Invalid --kind '{Kind}'. Expected 'all', 'action' or 'trigger'.", Kind);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IConnectorMetadataService>();
        var hits = await service.SearchOperationsAsync(Profile, Search, Connector, kind, Top, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteList(hits, PrintTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<OperationSearchHit> hits)
    {
        if (hits.Count == 0) { OutputWriter.WriteLine("No operations matched."); return; }

        var nameWidth = Math.Max(hits.Max(h => h.Name.Length), 20);
        var connectorWidth = Math.Max(hits.Max(h => (h.ConnectorName ?? "-").Length), 15);
        var header = $"{"Operation".PadRight(nameWidth)} | {"Connector".PadRight(connectorWidth)} | Summary";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var h in hits)
            OutputWriter.WriteLine($"{h.Name.PadRight(nameWidth)} | {(h.ConnectorName ?? "-").PadRight(connectorWidth)} | {h.DisplayName}");
    }
#pragma warning restore TXC003
}
