using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connector;

/// <summary>
/// <c>txc environment connector list</c> — the connector catalog of the
/// connected environment, custom connectors included.
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "list",
    Description = "Lists the connectors available in the LIVE connected environment, built-in and custom. Requires an active profile. The logical name is what 'connection create --connector' takes; for a custom connector it is stable across environments. Use 'connector get' to see a connector's operations."
)]
public class ConnectorListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorListCliCommand));

    [CliOption(Name = "--filter", Aliases = ["--search"], Description = "Show only connectors whose logical name, apiId or display name contains this text (case-insensitive).", Required = false)]
    public string? Filter { get; set; }

    [CliOption(Name = "--kind", Description = "Show only built-in or custom connectors: built-in or custom.", Required = false)]
    public string? Kind { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of connectors to return.", Required = false)]
    public int? Top { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectorService>();
        var connectors = await service.ListAsync(Profile, Filter, Kind?.Trim().ToLowerInvariant(), Top, CancellationToken.None)
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
                $"{Fit(connector.LogicalName, nameWidth).PadRight(nameWidth)} | {connector.Kind.PadRight(8)} | " +
                $"{Fit(connector.Tier ?? "-", 10).PadRight(10)} | {connector.DisplayName}");
        }
    }

    private static string Fit(string value, int width)
        => value.Length > width ? value[..(width - 1)] + "." : value;
#pragma warning restore TXC003
}
