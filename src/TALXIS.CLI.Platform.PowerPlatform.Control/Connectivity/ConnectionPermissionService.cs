using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>Shares connections with other principals and revokes that access.</summary>
internal sealed class ConnectionPermissionService : IConnectionPermissionService
{
    private readonly ConnectivityTargetResolver _targets;
    private readonly ConnectivityApiClient _client;
    private readonly ConnectorResolver _connectors;
    private readonly PrincipalResolver _principals;

    public ConnectionPermissionService(
        ConnectivityTargetResolver targets,
        ConnectivityApiClient client,
        ConnectorResolver connectors,
        PrincipalResolver principals)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _connectors = connectors ?? throw new ArgumentNullException(nameof(connectors));
        _principals = principals ?? throw new ArgumentNullException(nameof(principals));
    }

    public async Task<IReadOnlyList<ConnectionPermission>> ListPermissionsAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct)
    {
        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        return await FetchAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false);
    }

    public async Task<ConnectionPermissionOutcome> AddPermissionAsync(
        string? profileName,
        string connector,
        string connectionId,
        ConnectionPrincipalSpec principal,
        ConnectionPermissionRole role,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        var who = await _principals.ResolveAsync(target, principal, ct).ConfigureAwait(false);

        var existing = (await FetchAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false))
            .FirstOrDefault(who.Matches);

        if (existing?.Role == ConnectionPermissionRole.Owner)
            throw new ArgumentException($"{who.Description} owns this connection; the owner's access cannot be changed.");
        if (existing?.Role == role)
            return new ConnectionPermissionOutcome(ConnectionOutcomeStatus.Unchanged, connectionId, resolved.ApiId, existing);

        var request = ConnectionPermissionMapper.BuildPut(who.Id, who.Email, who.PlatformType, target.Connection.TenantId, role);
        await ModifyAsync(target, resolved.ApiId, connectionId, request, ct).ConfigureAwait(false);

        var granted = (await FetchAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false))
            .FirstOrDefault(who.Matches);
        return new ConnectionPermissionOutcome(
            existing is null ? ConnectionOutcomeStatus.Assigned : ConnectionOutcomeStatus.Updated,
            connectionId, resolved.ApiId, granted);
    }

    public async Task<ConnectionPermissionOutcome> RemovePermissionAsync(
        string? profileName,
        string connector,
        string connectionId,
        ConnectionPrincipalSpec principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var target = await _targets.ResolveAsync(profileName, ct).ConfigureAwait(false);
        var resolved = await _connectors.ResolveAsync(target, connector, ct).ConfigureAwait(false);
        var who = await _principals.ResolveAsync(target, principal, ct).ConfigureAwait(false);

        var existing = (await FetchAsync(target, resolved.ApiId, connectionId, ct).ConfigureAwait(false))
            .FirstOrDefault(who.Matches);
        if (existing is null)
            return new ConnectionPermissionOutcome(ConnectionOutcomeStatus.Unchanged, connectionId, resolved.ApiId, null);

        // The platform enforces this too, but a clear message beats its 403.
        if (existing.Role == ConnectionPermissionRole.Owner)
            throw new ArgumentException($"{who.Description} owns this connection; the owner cannot be removed.");

        var request = ConnectionPermissionMapper.BuildDelete(resolved.ApiId, connectionId, existing.PermissionId);
        await ModifyAsync(target, resolved.ApiId, connectionId, request, ct).ConfigureAwait(false);

        return new ConnectionPermissionOutcome(ConnectionOutcomeStatus.Removed, connectionId, resolved.ApiId, existing);
    }

    private async Task<IReadOnlyList<ConnectionPermission>> FetchAsync(
        ConnectivityTarget target, string apiId, string connectionId, CancellationToken ct)
    {
        using var response = await _client.SendForConnectionAsync(
            HttpMethod.Get,
            ConnectivityEndpoints.Permissions(target.BaseUri, apiId, connectionId, target.EnvironmentId),
            target, null, apiId, connectionId, ct).ConfigureAwait(false);

        return response.Body is { } doc ? ConnectionPermissionMapper.ParseList(doc.RootElement) : [];
    }

    private async Task ModifyAsync(
        ConnectivityTarget target, string apiId, string connectionId, JsonObject request, CancellationToken ct)
    {
        using var _ = await _client.SendForConnectionAsync(
            HttpMethod.Post,
            ConnectivityEndpoints.ModifyPermissions(target.BaseUri, apiId, connectionId, target.EnvironmentId),
            target, request, apiId, connectionId, ct).ConfigureAwait(false);
    }
}
