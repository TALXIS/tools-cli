using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connector.Operation;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector;

[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "get",
    Description = "Gets the operation index (actions, triggers, webhook triggers) of one Power Automate connector. A custom connector defined in the workspace (a Connectors.* project) is read from its local apiDefinition.swagger.json with no environment call; any other connector is read once from the LIVE connected environment (needs an active profile) and kept in the workspace's .txc/connectors/ snapshot, which later calls read instead. The answer's source says which (workspace, snapshot or environment). Use 'connector operation get' for exact parameter names before writing an action."
)]
public class ConnectorGetCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorGetCliCommand));

    [CliArgument(Name = "connector", Description = ConnectorDescriptions.Identifier)]
    public string Connector { get; set; } = null!;

    [CliOption(Name = "--filter", Description = "Show only operations whose ID or summary contains this text.", Required = false)]
    public string? Filter { get; set; }

    [CliOption(Name = "--refresh", Description = "Fetch from the environment again and rewrite the workspace snapshot in .txc/connectors/, instead of reading it. Use before a release, or when the connector changed.", Required = false)]
    public bool Refresh { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var connector = await ConnectorMetadataLookup.GetConnectorAsync(Profile, Connector, Filter, CancellationToken.None, Refresh)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(connector, PrintConnector);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintConnector(ConnectorDetail connector)
    {
        OutputWriter.WriteLine($"{connector.DisplayName ?? connector.LogicalName} ({connector.ApiId})");
        OutputWriter.WriteLine(OperationTextRenderer.SourceLine(connector.Source, connector.SourcePath));
        OutputWriter.WriteLine(connector.MatchedOperations is { } matched
            ? $"{matched} of {connector.OperationCount} operations matched"
            : $"{connector.OperationCount} operations");
        OutputWriter.WriteLine(string.Empty);

        if (connector.Operations.Count == 0) { OutputWriter.WriteLine("No operations matched."); return; }

        var idWidth = Math.Max(connector.Operations.Max(o => o.OperationId.Length), 20);
        var header = $"{"Operation".PadRight(idWidth)} | {"Kind".PadRight(15)} | Summary";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var o in connector.Operations)
        {
            var marker = o.IsDeprecated ? " [deprecated]" : string.Empty;
            OutputWriter.WriteLine($"{o.OperationId.PadRight(idWidth)} | {o.Kind.PadRight(15)} | {o.Summary}{marker}");
        }
    }
#pragma warning restore TXC003
}
