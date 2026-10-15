using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>
/// Searches operations across connectors, for when the connector is not yet known.
/// Usage: <c>txc environment connector operation search [--query &lt;text&gt;] [--connector &lt;name&gt;] [--kind all|actions|triggers] [--top &lt;n&gt;]</c>
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "search",
    Description = "Searches Power Automate operations across connectors in the LIVE connected environment. Requires an active profile. Deprecated operations are excluded."
)]
public class ConnectorOperationSearchCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorOperationSearchCliCommand));

    [CliOption(Name = "--query", Description = "Text to search for in operation names and descriptions.", Required = false)]
    public string? Query { get; set; }

    [CliOption(Name = "--connector", Description = "Limit the search to one connector. " + ConnectorDescriptions.Identifier, Required = false)]
    public string? Connector { get; set; }

    [CliOption(Name = "--kind", Description = "Which operations to return: all, actions, or triggers.", Required = false)]
    public string Kind { get; set; } = "all";

    [CliOption(Name = "--top", Description = "Maximum number of results to return.", Required = false)]
    public int Top { get; set; } = 20;

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPowerAutomateConnectorService>();
        var hits = await service.SearchOperationsAsync(Profile, Query, Connector, Kind, Top, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteList(hits, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<OperationSearchHit> hits)
    {
        if (hits.Count == 0)
        {
            OutputWriter.WriteLine("No operations matched.");
            return;
        }

        var nameWidth = Math.Clamp(hits.Max(h => h.Name.Length), 20, 45);
        var header = $"{"Operation".PadRight(nameWidth)} | {"Connector".PadRight(28)} | Summary";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var hit in hits)
        {
            var name = EnvironmentPrincipalCommandSupport.Truncate(hit.Name, nameWidth);
            var connector = EnvironmentPrincipalCommandSupport.Truncate(hit.ConnectorDisplayName ?? hit.ConnectorName ?? "-", 28);
            OutputWriter.WriteLine($"{name.PadRight(nameWidth)} | {connector.PadRight(28)} | {hit.DisplayName}");
        }
    }

#pragma warning restore TXC003
}
