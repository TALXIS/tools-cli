using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector;

[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Gets the operation index (actions, triggers, webhook triggers) of one Power Automate connector from the LIVE connected environment. Requires an active profile. Use 'connector operation get' for exact parameter names before writing an action."
)]
public class ConnectorGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorGetCliCommand));

    [CliArgument(Description = "Connector name, for example shared_commondataserviceforapps or shared_office365.")]
    public string Connector { get; set; } = string.Empty;

    [CliOption(Name = "--search", Description = "Show only operations whose ID or summary contains this text.", Required = false)]
    public string? Search { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectorMetadataService>();
        var connector = await service.GetConnectorAsync(Profile, Connector, Search, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteData(connector, PrintConnector);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintConnector(ConnectorDetail connector)
    {
        OutputWriter.WriteLine($"{connector.DisplayName ?? connector.Name} ({connector.ApiId})");
        OutputWriter.WriteLine(connector.MatchedOperations is { } matched
            ? $"{matched} of {connector.OperationCount} operations matched"
            : $"{connector.OperationCount} operations");
        OutputWriter.WriteLine(string.Empty);

        if (connector.Operations.Count == 0) { OutputWriter.WriteLine("No operations matched."); return; }

        var idWidth = Math.Clamp(connector.Operations.Max(o => o.OperationId.Length), 20, 50);
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
