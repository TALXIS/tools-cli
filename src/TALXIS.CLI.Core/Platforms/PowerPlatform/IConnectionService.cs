namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// A Power Platform connection: the stored authentication session for one
/// connector in one environment. Never exported with a solution — its id
/// differs on every environment. Solutions bind to a connection
/// <em>reference</em> instead.
/// </summary>
/// <param name="ConnectionId">Opaque connection name (do not normalise).</param>
/// <param name="ApiId">Short connector id, for example <c>shared_office365users</c>.</param>
/// <param name="ConnectionStatus">Platform status, for example <see cref="ConnectionStatusCodes.Connected"/> or <c>Error</c>.</param>
/// <param name="StatusMessage">Platform error detail when the status is not <see cref="ConnectionStatusCodes.Connected"/>.</param>
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
/// <see cref="ConnectionOutcomeStatus.Created"/>, <see cref="ConnectionOutcomeStatus.Updated"/>,
/// <see cref="ConnectionOutcomeStatus.Unchanged"/>, <see cref="ConnectionOutcomeStatus.ConsentRequired"/> or
/// <see cref="ConnectionOutcomeStatus.Failed"/>.
/// </param>
public sealed record ConnectionCreateOutcome(
    ConnectionOutcomeStatus Status,
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
/// <remarks>
/// <see cref="Parameters"/> holds secrets in clear text. Services never pass the
/// options to a log statement or an exception message, and callers must not either.
/// </remarks>
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

/// <summary>
/// Role a principal holds on a connection. Values are explicit: they are part of
/// the output contract. <see cref="ConnectionRoleNames"/> names them.
/// </summary>
public enum ConnectionPermissionRole
{
    /// <summary>Can use the connection (the portal's "Can use").</summary>
    CanView = 0,
    CanViewWithShare = 1,
    CanEdit = 2,

    /// <summary>The creator. Listed, but never granted or revoked.</summary>
    Owner = 3,
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
public sealed record ConnectionPermission(
    string PermissionId,
    string? PrincipalId,
    string? PrincipalType,
    string? DisplayName,
    string? Email,
    ConnectionPermissionRole Role);

/// <summary>Result of granting or revoking a permission.</summary>
/// <param name="Status">
/// <see cref="ConnectionOutcomeStatus.Assigned"/>, <see cref="ConnectionOutcomeStatus.Updated"/>,
/// <see cref="ConnectionOutcomeStatus.Unchanged"/> or <see cref="ConnectionOutcomeStatus.Removed"/>.
/// </param>
/// <param name="Permission">
/// The resulting permission; for <see cref="ConnectionOutcomeStatus.Removed"/> the one that
/// was revoked, and null when a removal found nothing to revoke.
/// </param>
public sealed record ConnectionPermissionOutcome(
    ConnectionOutcomeStatus Status,
    string ConnectionId,
    string ApiId,
    ConnectionPermission? Permission);

/// <summary>Reads the connections the caller owns or can use.</summary>
public interface IConnectionReader
{
    /// <summary>
    /// Connections the caller owns or can use. Connections created by another
    /// identity are only visible once shared.
    /// </summary>
    Task<IReadOnlyList<ConnectionSummary>> ListAsync(
        string? profileName, string? connector, string? filter, CancellationToken ct);

    /// <param name="connector">
    /// The connector of the connection; optional. When null the connection is located by id
    /// among the connections the caller owns or can use.
    /// </param>
    /// <exception cref="ConnectionNotFoundException">No such connection is visible to the caller.</exception>
    /// <exception cref="ConnectionAccessDeniedException">The caller may not read the connection.</exception>
    /// <exception cref="AmbiguousConnectionException">No connector was named and the id exists under several.</exception>
    Task<ConnectionSummary> GetAsync(
        string? profileName, string? connector, string connectionId, CancellationToken ct);
}

/// <summary>
/// Creates, tests and deletes connections in the environment bound to the
/// active profile. Backend choices (connectivity API, API Hub consent,
/// Dataverse connector table) are internal.
/// </summary>
public interface IConnectionService : IConnectionReader
{
    /// <summary>
    /// Idempotent by connector and display name: an existing connected
    /// connection is returned unchanged.
    /// </summary>
    Task<ConnectionCreateOutcome> CreateAsync(
        string? profileName, ConnectionCreateOptions options, CancellationToken ct);

    /// <inheritdoc cref="IConnectionReader.GetAsync"/>
    Task<ConnectionTestOutcome> TestAsync(
        string? profileName, string? connector, string connectionId, CancellationToken ct);

    /// <inheritdoc cref="IConnectionReader.GetAsync"/>
    Task DeleteAsync(
        string? profileName, string? connector, string connectionId, CancellationToken ct);
}

/// <summary>
/// Shares connections with other principals and revokes that access. Like
/// <see cref="IConnectionReader.GetAsync"/>, every method takes an optional connector
/// that only disambiguates the connection id.
/// </summary>
public interface IConnectionPermissionService
{
    Task<IReadOnlyList<ConnectionPermission>> ListPermissionsAsync(
        string? profileName, string? connector, string connectionId, CancellationToken ct);

    /// <summary>Grants or changes a principal's role. Idempotent.</summary>
    Task<ConnectionPermissionOutcome> AddPermissionAsync(
        string? profileName,
        string? connector,
        string connectionId,
        ConnectionPrincipalSpec principal,
        ConnectionPermissionRole role,
        CancellationToken ct);

    /// <summary>Revokes a principal's access. The owner cannot be revoked.</summary>
    Task<ConnectionPermissionOutcome> RemovePermissionAsync(
        string? profileName,
        string? connector,
        string connectionId,
        ConnectionPrincipalSpec principal,
        CancellationToken ct);
}
