using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.ConnectionReference;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Lists connection references in the LIVE connected environment. Requires an active profile. A solution-aware cloud flow binds each connector action by connection reference logical name."
)]
public class ConnectionReferenceListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionReferenceListCliCommand));

    [CliOption(Name = "--connector", Description = "Show only references for this connector, for example shared_commondataserviceforapps.", Required = false)]
    public string? Connector { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionReferenceService>();
        var rows = await service.ListAsync(Profile, Connector, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectionReferenceRecord> rows)
    {
        if (rows.Count == 0) { OutputWriter.WriteLine("No connection references found."); return; }

        var nameWidth = Math.Max(rows.Max(r => r.LogicalName.Length), 20);
        var connectorWidth = Math.Max(rows.Max(r => ConnectorName(r).Length), 15);
        var header = $"{"Logical name".PadRight(nameWidth)} | {"Connector".PadRight(connectorWidth)} | Bound";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            var bound = string.IsNullOrWhiteSpace(r.ConnectionId) ? "no" : "yes";
            OutputWriter.WriteLine($"{r.LogicalName.PadRight(nameWidth)} | {ConnectorName(r).PadRight(connectorWidth)} | {bound}");
        }
    }

    /// <summary>The connector name (<c>shared_teams</c>), not the full api id, so rows stay distinguishable.</summary>
    private static string ConnectorName(ConnectionReferenceRecord r)
        => r.ConnectorId is { } id ? id[(id.TrimEnd('/').LastIndexOf('/') + 1)..] : "-";
#pragma warning restore TXC003
}
