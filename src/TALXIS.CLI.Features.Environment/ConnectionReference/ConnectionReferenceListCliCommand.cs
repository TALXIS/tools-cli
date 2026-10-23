using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
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
        var references = await service.ListAsync(Profile, Connector, CancellationToken.None).ConfigureAwait(false);

        // The connection behind each bound reference says whose it is and
        // whether it works, which is what tells two references apart.
        var metadata = TxcServices.Get<IConnectorMetadataService>();
        var rows = new List<ConnectionReferenceView>();
        foreach (var r in references)
        {
            var connector = ConnectorName(r);
            var connection = string.IsNullOrWhiteSpace(r.ConnectionId)
                ? null
                : await TryGetConnectionAsync(metadata, connector, r.ConnectionId).ConfigureAwait(false);

            rows.Add(new ConnectionReferenceView(
                r.LogicalName, r.DisplayName, connector, r.ConnectionId,
                connection is null ? null : TALXIS.CLI.Features.Environment.Connector.Operation.DynamicValuesQuery.Owner(connection),
                connection?.Status ?? (string.IsNullOrWhiteSpace(r.ConnectionId) ? "not bound" : null)));
        }

        OutputFormatter.WriteList(rows, PrintTable);
        return ExitSuccess;
    }

    private async Task<ConnectionDetail?> TryGetConnectionAsync(IConnectorMetadataService metadata, string connector, string connectionId)
    {
        try
        {
            return await metadata.GetConnectionAsync(Profile, connector, connectionId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // A connection the caller cannot see still lists as bound.
            Logger.LogDebug(ex, "Could not read connection {Connection}.", connectionId);
            return null;
        }
    }

#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<ConnectionReferenceView> rows)
    {
        if (rows.Count == 0) { OutputWriter.WriteLine("No connection references found."); return; }

        var nameWidth = Math.Max(rows.Max(r => r.LogicalName.Length), 20);
        var displayWidth = Math.Max(rows.Max(r => (r.DisplayName ?? "-").Length), 12);
        var connectorWidth = Math.Max(rows.Max(r => r.Connector.Length), 15);
        var header = $"{"Logical name".PadRight(nameWidth)} | {"Display name".PadRight(displayWidth)} | {"Connector".PadRight(connectorWidth)} | {"Status".PadRight(10)} | Owner";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
            OutputWriter.WriteLine($"{r.LogicalName.PadRight(nameWidth)} | {(r.DisplayName ?? "-").PadRight(displayWidth)} | {r.Connector.PadRight(connectorWidth)} | {(r.Status ?? "-").PadRight(10)} | {r.Owner ?? "-"}");
    }
#pragma warning restore TXC003

    /// <summary>The connector name (<c>shared_teams</c>), not the full api id, so rows stay distinguishable.</summary>
    private static string ConnectorName(ConnectionReferenceRecord r)
        => r.ConnectorId is { } id ? id[(id.TrimEnd('/').LastIndexOf('/') + 1)..] : "-";
}

/// <summary>A connection reference with the owner and status of the connection behind it.</summary>
public sealed record ConnectionReferenceView(
    string LogicalName,
    string? DisplayName,
    string Connector,
    string? ConnectionId,
    string? Owner,
    string? Status);
