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
    Description = "List the connectors available in the profile's environment."
)]
public class ConnectorListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectorListCliCommand));

    [CliOption(Name = "--filter", Description = "Show only connectors whose name or display name contains this substring.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectorCatalogService>();
        IReadOnlyList<ConnectorSummary> connectors = await service.ListConnectorsAsync(Profile, CancellationToken.None)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            connectors = connectors
                .Where(c => Contains(c.Name, Filter) || Contains(c.DisplayName, Filter))
                .ToList();
        }

        OutputFormatter.WriteList(connectors, PrintTable);
        return ExitSuccess;
    }

    private static bool Contains(string? value, string substring)
        => value is not null && value.Contains(substring, StringComparison.OrdinalIgnoreCase);

    // Text-renderer callback invoked by OutputFormatter.WriteList, so OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectorSummary> connectors)
    {
        if (connectors.Count == 0)
        {
            OutputWriter.WriteLine("No connectors found.");
            return;
        }

        int nameWidth = Math.Clamp(connectors.Max(c => c.Name.Length), 20, 60);
        int displayWidth = Math.Clamp(connectors.Max(c => c.DisplayName.Length), 12, 40);
        string header = $"{"Name".PadRight(nameWidth)} | {"Display Name".PadRight(displayWidth)} | {"Tier".PadRight(10)} | Custom";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var c in connectors)
        {
            string name = c.Name.Length > nameWidth ? c.Name[..(nameWidth - 1)] + "." : c.Name;
            string display = c.DisplayName.Length > displayWidth ? c.DisplayName[..(displayWidth - 1)] + "." : c.DisplayName;
            OutputWriter.WriteLine(
                $"{name.PadRight(nameWidth)} | {display.PadRight(displayWidth)} | {(c.Tier ?? string.Empty).PadRight(10)} | {(c.IsCustomApi ? "yes" : string.Empty)}");
        }
    }
#pragma warning restore TXC003
}
