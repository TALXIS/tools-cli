namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// A Power Platform connection: the stored authentication session for one
/// connector in one environment. Never exported with a solution — its id
/// differs on every environment. Solutions bind to a connection
/// <em>reference</em> instead.
/// </summary>
/// <param name="ConnectionId">Opaque connection name (do not normalise).</param>
/// <param name="ApiId">Short connector id, for example <c>shared_office365users</c>.</param>
/// <param name="ConnectionStatus">Platform status, for example <c>Connected</c> or <c>Error</c>.</param>
/// <param name="StatusMessage">Platform error detail when the status is not <c>Connected</c>.</param>
public sealed record ConnectionSummary(
    string ConnectionId,
    string ApiId,
    string? DisplayName,
    string? ConnectionStatus,
    string? StatusMessage,
    string? CreatedByType,
    string? CreatedBy,
    DateTimeOffset? CreatedTime);

/// <summary>Result of creating (or finding) a connection.</summary>
/// <param name="Status">
/// <c>created</c>, <c>updated</c>, <c>unchanged</c>, <c>consent-required</c>
/// (the connection exists but needs a person to complete consent) or
/// <c>failed</c> (the platform rejected the credentials).
/// </param>
public sealed record ConnectionCreateOutcome(
    string Status,
    string ConnectionId,
    string ApiId,
    string DisplayName,
    string? ConnectionStatus,
    string? ConsentLink,
    string? Message);

/// <summary>Input to <see cref="IConnectionService.CreateAsync"/>.</summary>
/// <param name="Connector">Logical name, apiId or exact display name of the connector.</param>
/// <param name="AuthMethod">Named connection parameter set; null picks the connector's default.</param>
/// <param name="Parameters">Connection parameter values, secrets included. Never logged.</param>
/// <param name="AttemptConsent">Try to complete OAuth consent silently for user credentials.</param>
public sealed record ConnectionCreateOptions(
    string Connector,
    string DisplayName,
    string? AuthMethod,
    IReadOnlyDictionary<string, string> Parameters,
    bool AttemptConsent);

/// <summary>Outcome of <see cref="IConnectionService.TestAsync"/>.</summary>
public sealed record ConnectionTestOutcome(
    bool Healthy,
    string? ConnectionStatus,
    string? Message);

/// <summary>Role a principal holds on a connection. Values are explicit: they are part of the output contract.</summary>
public enum ConnectionPermissionRole
{
    /// <summary>Can use the connection (the portal's "Can use").</summary>
    CanView = 0,
    CanViewWithShare = 1,
    CanEdit = 2,
}

/// <summary>Kind of principal a connection can be shared with.</summary>
public enum ConnectionPrincipalKind
{
    User = 0,
    ServicePrincipal = 1,
    Group = 2,
}

/// <summary>
/// Who to share with. <paramref name="Identifier"/> is the form the caller
/// has: user principal name or Entra object id, service principal client id
/// or object id, or group object id.
/// </summary>
public sealed record ConnectionPrincipalSpec(ConnectionPrincipalKind Kind, string Identifier);

/// <summary>One entry of a connection's permission list.</summary>
/// <param name="PermissionId">
/// The permission row id. For service principals this is <em>not</em> the
/// principal's object id; it is what removal is keyed by.
/// </param>
/// <param name="Role">Platform role: <c>Owner</c>, <c>CanEdit</c>, <c>CanViewWithShare</c> or <c>CanView</c>.</param>
public sealed record ConnectionPermission(
    string PermissionId,
    string? PrincipalId,
    string? PrincipalType,
    string? DisplayName,
    string? Email,
    string Role);

/// <summary>Result of granting or revoking a permission.</summary>
/// <param name="Status"><c>assigned</c>, <c>updated</c>, <c>unchanged</c> or <c>removed</c>.</param>
/// <param name="Permission">
/// The resulting permission; for <c>removed</c> the one that was revoked, and
/// null when a removal found nothing to revoke.
/// </param>
public sealed record ConnectionPermissionOutcome(
    string Status,
    string ConnectionId,
    string ApiId,
    ConnectionPermission? Permission);

/// <summary>
/// Creates, inspects, tests, shares and deletes connections in the
/// environment bound to the active profile. Backend choices (connectivity
/// API, API Hub consent, Dataverse connector table) are internal.
/// </summary>
public interface IConnectionService
{
    /// <summary>
    /// Connections the caller owns or can use. Connections created by another
    /// identity are only visible once shared.
    /// </summary>
    Task<IReadOnlyList<ConnectionSummary>> ListAsync(
        string? profileName, string? connector, string? filter, CancellationToken ct);

    Task<ConnectionSummary> GetAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct);

    /// <summary>
    /// Idempotent by connector and display name: an existing connected
    /// connection is returned unchanged.
    /// </summary>
    Task<ConnectionCreateOutcome> CreateAsync(
        string? profileName, ConnectionCreateOptions options, CancellationToken ct);

    Task<ConnectionTestOutcome> TestAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct);

    Task DeleteAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct);

    Task<IReadOnlyList<ConnectionPermission>> ListPermissionsAsync(
        string? profileName, string connector, string connectionId, CancellationToken ct);

    /// <summary>Grants or changes a principal's role. Idempotent.</summary>
    Task<ConnectionPermissionOutcome> AddPermissionAsync(
        string? profileName,
        string connector,
        string connectionId,
        ConnectionPrincipalSpec principal,
        ConnectionPermissionRole role,
        CancellationToken ct);

    /// <summary>Revokes a principal's access. The owner cannot be revoked.</summary>
    Task<ConnectionPermissionOutcome> RemovePermissionAsync(
        string? profileName,
        string connector,
        string connectionId,
        ConnectionPrincipalSpec principal,
        CancellationToken ct);
}
