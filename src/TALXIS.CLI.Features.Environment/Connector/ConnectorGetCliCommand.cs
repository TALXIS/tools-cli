using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// <c>txc environment connector get</c> — a connector's operation index.
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "get",
    Description = "Gets the operation index for one Power Automate connector from the LIVE connected environment. Requires an active profile. Returns operation IDs only; use 'connector operation get' for exact parameter specs before writing an action."
)]
public class ConnectorGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorGetCliCommand));

    [CliArgument(Description = "Connector name, for example shared_teams or shared_commondataserviceforapps.")]
    public string Connector { get; set; } = string.Empty;

    [CliOption(Name = "--query", Description = "Show only operations whose ID or summary contains this text.", Required = false)]
    public string? Query { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IPowerAutomateConnectorService>();
        var connector = await service.GetConnectorAsync(Profile, Connector, Query, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(connector, PrintConnector);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintConnector(ConnectorDetail connector)
    {
        OutputWriter.WriteLine($"{connector.DisplayName ?? connector.Name} ({connector.Name})");
        OutputWriter.WriteLine(connector.MatchedOperations is { } matched
            ? $"{matched} of {connector.OperationCount} operations matched"
            : $"{connector.OperationCount} operations");
        OutputWriter.WriteLine(string.Empty);

        if (connector.Operations.Count == 0)
        {
            OutputWriter.WriteLine("No operations matched.");
            return;
        }

        var idWidth = Math.Clamp(connector.Operations.Max(o => o.OperationId.Length), 20, 50);
        var header = $"{"Operation".PadRight(idWidth)} | {"Method".PadRight(6)} | Summary";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var operation in connector.Operations)
        {
            var id = operation.OperationId.Length > idWidth
                ? operation.OperationId[..(idWidth - 1)] + "."
                : operation.OperationId;
            var marker = operation.IsDeprecated ? " [deprecated]" : operation.IsTrigger ? " [trigger]" : string.Empty;
            OutputWriter.WriteLine($"{id.PadRight(idWidth)} | {operation.Method.PadRight(6)} | {operation.Summary}{marker}");
        }
    }
#pragma warning restore TXC003
}
