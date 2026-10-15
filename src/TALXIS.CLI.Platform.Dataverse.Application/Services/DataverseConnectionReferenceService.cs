using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

/// <summary>
/// Connection references over the Dataverse <c>connectionreference</c> table.
/// Connector resolution and connection status come from the connector and
/// connection services through their Core contracts, so this layer needs no
/// knowledge of the connectivity API.
/// </summary>
internal sealed class DataverseConnectionReferenceService : IConnectionReferenceService
{
    private readonly IConnectorResolver _connectors;
    private readonly IConnectionService _connections;

    public DataverseConnectionReferenceService(IConnectorResolver connectors, IConnectionService connections)
    {
        _connectors = connectors ?? throw new ArgumentNullException(nameof(connectors));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<IReadOnlyList<ConnectionReferenceRecord>> ListAsync(
        string? profileName, ConnectionReferenceListOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        ResolvedConnectorInfo? connector = null;
        if (!string.IsNullOrWhiteSpace(options.Connector))
            connector = await _connectors.ResolveAsync(profileName, options.Connector, ct).ConfigureAwait(false);

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var rows = await ConnectionReferenceManager.ListAsync(
            conn.Client,
            options.Solution,
            connector is null ? null : ConnectionReferencePlanner.ToStoredConnectorId(connector.ApiId),
            connector?.ConnectorRowId,
            options.ConnectionId,
            options.Filter,
            ct).ConfigureAwait(false);

        var records = await ToRecordsAsync(profileName, rows, ct).ConfigureAwait(false);
        return options.UnboundOnly
            ? records.Where(r => r.BindingStatus == ConnectionBindingStatus.Unbound).ToList()
            : records;
    }

    public async Task<ConnectionReferenceRecord> GetAsync(string? profileName, string logicalName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var row = await ConnectionReferenceManager.FindAsync(conn.Client, logicalName, ct).ConfigureAwait(false)
            ?? throw NotFound(logicalName);

        return (await ToRecordsAsync(profileName, [row], ct).ConfigureAwait(false))[0];
    }

    public async Task<ConnectionReferenceOutcome> CreateAsync(
        string? profileName, ConnectionReferenceCreateOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.LogicalName))
            throw new ArgumentException("A logical name is required.", nameof(options));

        var connector = await _connectors.ResolveAsync(profileName, options.Connector, ct).ConfigureAwait(false);

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var existingRow = await ConnectionReferenceManager.FindAsync(conn.Client, options.LogicalName, ct).ConfigureAwait(false);
        var existing = existingRow is null ? null : (await ToRecordsAsync(profileName, [existingRow], ct).ConfigureAwait(false))[0];

        switch (ConnectionReferencePlanner.PlanCreate(existing, connector.ApiId, options.ConnectionId))
        {
            case ConnectionReferencePlanner.CreateAction.Unchanged:
                return new ConnectionReferenceOutcome("unchanged", existing!);

            case ConnectionReferencePlanner.CreateAction.Bind:
                await WriteAsync(connector.ApiId, options.ConnectionId, () =>
                    ConnectionReferenceManager.UpdateAsync(conn.Client, existing!.Id, options.ConnectionId, null, ct)).ConfigureAwait(false);
                return new ConnectionReferenceOutcome("updated", await GetAsync(profileName, options.LogicalName, ct).ConfigureAwait(false));

            default:
                if (connector.IsCustom && connector.ConnectorRowId is null)
                {
                    throw new ArgumentException(
                        $"The custom connector '{connector.LogicalName}' is not deployed in this environment yet. " +
                        "Import the solution that contains the connector before creating references to it.");
                }

                await WriteAsync(connector.ApiId, options.ConnectionId, () =>
                    ConnectionReferenceManager.CreateAsync(
                        conn.Client,
                        options,
                        ConnectionReferencePlanner.ToStoredConnectorId(connector.ApiId),
                        connector.IsCustom ? connector.ConnectorRowId : null,
                        ct)).ConfigureAwait(false);

                return new ConnectionReferenceOutcome("created", await GetAsync(profileName, options.LogicalName, ct).ConfigureAwait(false));
        }
    }

    public async Task<ConnectionReferenceOutcome> UpdateAsync(
        string? profileName, string logicalName, string? connectionId, string? displayName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionId) && string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Nothing to update: pass a connection id and/or a display name.");

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var row = await ConnectionReferenceManager.FindAsync(conn.Client, logicalName, ct).ConfigureAwait(false)
            ?? throw NotFound(logicalName);
        var current = (await ToRecordsAsync(profileName, [row], ct).ConfigureAwait(false))[0];

        if (ConnectionReferencePlanner.IsNoOp(current, connectionId, displayName))
            return new ConnectionReferenceOutcome("unchanged", current);

        // The platform validates the binding as the caller and rejects a connection
        // that does not exist or is not shared with them (ConnectionAuthorizationFailed).
        await WriteAsync(current.ApiId, connectionId, () =>
            ConnectionReferenceManager.UpdateAsync(conn.Client, row.Id, connectionId, displayName, ct)).ConfigureAwait(false);
        return new ConnectionReferenceOutcome("updated", await GetAsync(profileName, logicalName, ct).ConfigureAwait(false));
    }

    public async Task DeleteAsync(string? profileName, string logicalName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var row = await ConnectionReferenceManager.FindAsync(conn.Client, logicalName, ct).ConfigureAwait(false)
            ?? throw NotFound(logicalName);

        await ConnectionReferenceManager.DeleteAsync(conn.Client, row.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds each reference's binding status by reading the bound connection once
    /// per distinct connection.
    /// </summary>
    private async Task<IReadOnlyList<ConnectionReferenceRecord>> ToRecordsAsync(
        string? profileName, IReadOnlyList<ConnectionReferenceRow> rows, CancellationToken ct)
    {
        var statuses = new Dictionary<(string Api, string Connection), (string? Status, bool Readable)>();
        var records = new List<ConnectionReferenceRecord>(rows.Count);

        foreach (var row in rows)
        {
            var apiId = ConnectionReferencePlanner.ToShortApiId(row.StoredConnectorId);
            string? status = null;
            var readable = false;

            if (!string.IsNullOrWhiteSpace(row.ConnectionId) && apiId.Length > 0)
            {
                var key = (apiId, row.ConnectionId);
                if (!statuses.TryGetValue(key, out var cached))
                {
                    cached = await ReadConnectionStatusAsync(profileName, apiId, row.ConnectionId, ct).ConfigureAwait(false);
                    statuses[key] = cached;
                }

                (status, readable) = cached;
            }

            var binding = ConnectionReferencePlanner.Classify(row.ConnectionId, status, readable);
            records.Add(new ConnectionReferenceRecord(
                row.Id,
                row.LogicalName,
                row.DisplayName,
                apiId,
                row.ConnectionId,
                binding,
                BindingMessage(binding, status),
                row.IsManaged,
                row.Owner));
        }

        return records;
    }

    private async Task<(string? Status, bool Readable)> ReadConnectionStatusAsync(
        string? profileName, string apiId, string connectionId, CancellationToken ct)
    {
        try
        {
            var connection = await _connections.GetAsync(profileName, apiId, connectionId, ct).ConfigureAwait(false);
            return (connection.ConnectionStatus, true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Deleted, or owned by someone it has not been shared with.
            return (null, false);
        }
    }

    private static string? BindingMessage(string binding, string? status) => binding switch
    {
        ConnectionBindingStatus.Broken => $"The connection is {status ?? "not connected"}; fix or replace it.",
        ConnectionBindingStatus.Unknown => "The connection was not found or is not visible to you.",
        _ => null,
    };

    /// <summary>Runs a write and replaces the platform's binding failures with actionable messages.</summary>
    private static async Task WriteAsync(string apiId, string? connectionId, Func<Task> write)
    {
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (Exception ex) when (ConnectionReferencePlanner.TryTranslateBindingFailure(ex, connectionId, apiId, out var friendly))
        {
            throw friendly;
        }
    }

    private static ArgumentException NotFound(string logicalName)
        => new($"No connection reference named '{logicalName}' was found.");
}
