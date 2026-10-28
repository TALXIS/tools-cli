using Microsoft.PowerPlatform.Dataverse.Client;
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
    private readonly IConnectionReader _connections;

    public DataverseConnectionReferenceService(IConnectorResolver connectors, IConnectionReader connections)
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
            connector is null ? null : ConnectorIds.ToPath(connector.ApiId),
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
        var row = await FindRequiredAsync(conn.Client, logicalName, ct).ConfigureAwait(false);

        return await ToRecordAsync(profileName, row, ct).ConfigureAwait(false);
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
        var existing = existingRow is null ? null : await ToRecordAsync(profileName, existingRow, ct).ConfigureAwait(false);

        var action = ConnectionReferencePlanner.PlanCreate(existing, connector.ApiId, options.ConnectionId);
        return action == ConnectionReferencePlanner.CreateAction.Create
            ? await CreateNewAsync(profileName, conn.Client, connector, options, ct).ConfigureAwait(false)
            : await ReconcileExistingAsync(profileName, conn.Client, existingRow!, existing!, action, options, ct).ConfigureAwait(false);
    }

    public async Task<ConnectionReferenceOutcome> UpdateAsync(
        string? profileName, string logicalName, string? connectionId, string? displayName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionId) && string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Nothing to update: pass a connection id and/or a display name.");

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var row = await FindRequiredAsync(conn.Client, logicalName, ct).ConfigureAwait(false);
        var current = await ToRecordAsync(profileName, row, ct).ConfigureAwait(false);

        if (ConnectionReferencePlanner.IsNoOp(current, connectionId, displayName))
            return new ConnectionReferenceOutcome(ConnectionOutcomeStatus.Unchanged, current);

        // The platform validates the binding as the caller and rejects a connection
        // that does not exist or is not shared with them.
        await ConnectionBindingErrors.TranslateAsync(current.ApiId, connectionId, async () =>
        {
            await ConnectionReferenceManager.UpdateAsync(conn.Client, row.Id, connectionId, displayName, ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

        var updated = await ApplyAsync(profileName, row, current, connectionId, displayName, ct).ConfigureAwait(false);
        return new ConnectionReferenceOutcome(ConnectionOutcomeStatus.Updated, updated);
    }

    public async Task DeleteAsync(string? profileName, string logicalName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var row = await FindRequiredAsync(conn.Client, logicalName, ct).ConfigureAwait(false);

        await ConnectionReferenceManager.DeleteAsync(conn.Client, row.Id, ct).ConfigureAwait(false);
    }

    private async Task<ConnectionReferenceOutcome> CreateNewAsync(
        string? profileName,
        IOrganizationServiceAsync2 client,
        ResolvedConnectorInfo connector,
        ConnectionReferenceCreateOptions options,
        CancellationToken ct)
    {
        if (connector.IsCustom && connector.ConnectorRowId is null)
        {
            throw new ArgumentException(
                $"The custom connector '{connector.LogicalName}' is not deployed in this environment yet. " +
                "Import the solution that contains the connector before creating references to it.");
        }

        var storedConnectorId = ConnectorIds.ToPath(connector.ApiId);
        var customConnectorId = connector.IsCustom ? connector.ConnectorRowId : null;
        var id = await ConnectionBindingErrors.TranslateAsync(connector.ApiId, options.ConnectionId, () =>
            ConnectionReferenceManager.CreateAsync(client, options, storedConnectorId, customConnectorId, ct)).ConfigureAwait(false);

        var row = new ConnectionReferenceRow(
            id, options.LogicalName, options.DisplayName, storedConnectorId, customConnectorId,
            options.ConnectionId, IsManaged: false, Owner: null);
        return new ConnectionReferenceOutcome(
            ConnectionOutcomeStatus.Created, await ToRecordAsync(profileName, row, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The reference exists already: bind it when a different connection was
    /// asked for, and add it to the requested solution when that does not
    /// contain it yet.
    /// </summary>
    private async Task<ConnectionReferenceOutcome> ReconcileExistingAsync(
        string? profileName,
        IOrganizationServiceAsync2 client,
        ConnectionReferenceRow row,
        ConnectionReferenceRecord existing,
        ConnectionReferencePlanner.CreateAction action,
        ConnectionReferenceCreateOptions options,
        CancellationToken ct)
    {
        var record = existing;
        if (action == ConnectionReferencePlanner.CreateAction.Bind)
        {
            await ConnectionBindingErrors.TranslateAsync(existing.ApiId, options.ConnectionId, async () =>
            {
                await ConnectionReferenceManager.UpdateAsync(client, existing.Id, options.ConnectionId, null, ct).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            record = await ApplyAsync(profileName, row, existing, options.ConnectionId, null, ct).ConfigureAwait(false);
        }

        var addedToSolution = await EnsureInSolutionAsync(client, existing.Id, options.SolutionUniqueName, ct).ConfigureAwait(false);
        return new ConnectionReferenceOutcome(ConnectionReferencePlanner.OutcomeOf(action, addedToSolution), record);
    }

    /// <returns>True when the reference had to be added to the solution.</returns>
    private static async Task<bool> EnsureInSolutionAsync(
        IOrganizationServiceAsync2 client, Guid id, string? solutionUniqueName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(solutionUniqueName)
            || await ConnectionReferenceManager.IsInSolutionAsync(client, id, solutionUniqueName, ct).ConfigureAwait(false))
        {
            return false;
        }

        await ConnectionReferenceManager.AddToSolutionAsync(client, id, solutionUniqueName, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The record after a successful write, built from the values written. The
    /// binding status is read again only when the connection changed.
    /// </summary>
    private async Task<ConnectionReferenceRecord> ApplyAsync(
        string? profileName,
        ConnectionReferenceRow row,
        ConnectionReferenceRecord current,
        string? connectionId,
        string? displayName,
        CancellationToken ct)
    {
        var renamed = string.IsNullOrWhiteSpace(displayName) ? current : current with { DisplayName = displayName };
        if (string.IsNullOrWhiteSpace(connectionId) || string.Equals(current.ConnectionId, connectionId, StringComparison.Ordinal))
            return renamed;

        return await ToRecordAsync(profileName, row with { ConnectionId = connectionId, DisplayName = renamed.DisplayName }, ct)
            .ConfigureAwait(false);
    }

    private static async Task<ConnectionReferenceRow> FindRequiredAsync(
        IOrganizationServiceAsync2 client, string logicalName, CancellationToken ct)
        => await ConnectionReferenceManager.FindAsync(client, logicalName, ct).ConfigureAwait(false)
            ?? throw new ArgumentException($"No connection reference named '{logicalName}' was found.");

    private async Task<ConnectionReferenceRecord> ToRecordAsync(
        string? profileName, ConnectionReferenceRow row, CancellationToken ct)
        => (await ToRecordsAsync(profileName, [row], ct).ConfigureAwait(false))[0];

    /// <summary>
    /// Adds each reference's binding status by reading the bound connection once
    /// per distinct connection.
    /// </summary>
    private async Task<IReadOnlyList<ConnectionReferenceRecord>> ToRecordsAsync(
        string? profileName, IReadOnlyList<ConnectionReferenceRow> rows, CancellationToken ct)
    {
        var statuses = new Dictionary<(string Api, string Connection), (string? Status, bool Readable, string? Owner)>();
        var records = new List<ConnectionReferenceRecord>(rows.Count);

        foreach (var row in rows)
        {
            var apiId = ConnectorIds.ToShortOrEmpty(row.StoredConnectorId);
            string? status = null, connectionOwner = null;
            var readable = false;

            if (!string.IsNullOrWhiteSpace(row.ConnectionId) && apiId.Length > 0)
            {
                var key = (apiId, row.ConnectionId);
                if (!statuses.TryGetValue(key, out var cached))
                {
                    cached = await ReadConnectionStatusAsync(profileName, apiId, row.ConnectionId, ct).ConfigureAwait(false);
                    statuses[key] = cached;
                }

                (status, readable, connectionOwner) = cached;
            }

            var binding = ConnectionReferencePlanner.Classify(row.ConnectionId, status, readable);
            records.Add(new ConnectionReferenceRecord(
                row.Id, row.LogicalName, row.DisplayName, apiId, row.ConnectionId,
                binding, BindingMessage(binding, status), row.IsManaged, row.Owner, connectionOwner));
        }

        return records;
    }

    private async Task<(string? Status, bool Readable, string? Owner)> ReadConnectionStatusAsync(
        string? profileName, string apiId, string connectionId, CancellationToken ct)
    {
        try
        {
            var connection = await _connections.GetAsync(profileName, apiId, connectionId, ct).ConfigureAwait(false);
            return (connection.ConnectionStatus, true, connection.CreatedBy);
        }
        catch (Exception ex) when (ex is ConnectionNotFoundException or ConnectionAccessDeniedException or ConnectorNotFoundException)
        {
            // Deleted, or owned by someone it has not been shared with.
            return (null, false, null);
        }
    }

    private static string? BindingMessage(string binding, string? status) => binding switch
    {
        ConnectionBindingStatus.Broken => $"The connection is {status ?? "not connected"}; fix or replace it.",
        ConnectionBindingStatus.Unknown => "The connection was not found or is not visible to you.",
        _ => null,
    };
}
