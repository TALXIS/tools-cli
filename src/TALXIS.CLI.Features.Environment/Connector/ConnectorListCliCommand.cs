using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// Lists the connector catalog of the connected environment, custom connectors included.
/// Usage: <c>txc environment connector list [--filter &lt;text&gt;] [--kind built-in|custom] [--top &lt;n&gt;]</c>
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "list",
    Description = "Lists the connectors available in the LIVE connected environment, built-in and custom. Requires an active profile. The logical name is what 'connection create --connector' takes; for a custom connector it is stable across environments. Use 'connector get' to see a connector's operations."
)]
public class ConnectorListCliCommand : ConnectivityCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only connectors whose logical name, apiId or display name contains this text (case-insensitive).", Required = false)]
    public string? Filter { get; set; }

    [CliOption(Name = "--kind", Description = "Show only connectors of this kind: built-in or custom.", Required = false)]
    public string? Kind { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of connectors to return.", Required = false)]
    public int? Top { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var kind = string.IsNullOrWhiteSpace(Kind) ? null : Kind.Trim().ToLowerInvariant();
        if (kind is not null && !ConnectorKind.IsKnown(kind))
        {
            Logger.LogError("Invalid --kind '{Kind}'. Expected '{BuiltIn}' or '{Custom}'.", Kind, ConnectorKind.BuiltIn, ConnectorKind.Custom);
            return ExitValidationError;
        }

        var service = TxcServices.Get<IConnectorService>();
        var connectors = await service.ListAsync(Profile, Filter, kind, Top, CancellationToken.None)
            .ConfigureAwait(false);

        OutputFormatter.WriteList(connectors, PrintTable);
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectorListItem> connectors)
    {
        if (connectors.Count == 0)
        {
            OutputWriter.WriteLine("No connectors found.");
            return;
        }

        var nameWidth = Math.Clamp(connectors.Max(c => c.LogicalName.Length), 20, 50);
        var header = $"{"Logical name".PadRight(nameWidth)} | {"Kind".PadRight(8)} | {"Tier".PadRight(10)} | Display name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var connector in connectors)
        {
            OutputWriter.WriteLine(
                $"{TableText.Truncate(connector.LogicalName, nameWidth).PadRight(nameWidth)} | {connector.Kind.PadRight(8)} | " +
                $"{TableText.Truncate(connector.Tier ?? "-", 10).PadRight(10)} | {connector.DisplayName}");
        }
    }

#pragma warning restore TXC003
}
