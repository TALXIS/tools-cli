using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>How a connection reference relates to the connection it points at.</summary>
public static class ConnectionBindingStatus
{
    /// <summary>No connection is bound; flows using the reference cannot run.</summary>
    public const string Unbound = "unbound";

    /// <summary>Bound to a connection that is connected.</summary>
    public const string Bound = "bound";

    /// <summary>Bound to a connection that exists but is not connected (expired, revoked, bad credentials).</summary>
    public const string Broken = "broken";

    /// <summary>Bound, but the connection could not be read: it was deleted or is not visible to the caller.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// A connection reference: the ALM-safe placeholder flows and apps bind to
/// instead of a connection. Its logical name is stable across environments;
/// the connection it points at is not.
/// </summary>
/// <param name="ApiId">Short connector id of the connector the reference is for.</param>
/// <param name="ConnectionId">The bound connection, or null when unbound.</param>
/// <param name="BindingStatus">One of <see cref="ConnectionBindingStatus"/>.</param>
public sealed record ConnectionReferenceRecord(
    Guid Id,
    string LogicalName,
    string? DisplayName,
    string ApiId,
    string? ConnectionId,
    string BindingStatus,
    string? BindingMessage,
    bool IsManaged,
    string? Owner);

public sealed record ConnectionReferenceListOptions(
    string? Solution,
    string? Connector,
    string? ConnectionId,
    bool UnboundOnly,
    string? Filter);

/// <param name="Connector">Logical name, apiId or exact display name; resolved for the environment.</param>
/// <param name="SolutionUniqueName">Solution the reference is added to; null leaves it in the default solution only.</param>
public sealed record ConnectionReferenceCreateOptions(
    string LogicalName,
    string DisplayName,
    string Connector,
    string? ConnectionId,
    string? SolutionUniqueName);

/// <param name="Status">
/// <see cref="ConnectionOutcomeStatus.Created"/>, <see cref="ConnectionOutcomeStatus.Updated"/> or
/// <see cref="ConnectionOutcomeStatus.Unchanged"/>.
/// </param>
public sealed record ConnectionReferenceOutcome(ConnectionOutcomeStatus Status, ConnectionReferenceRecord Reference);

/// <summary>
/// Connection references in the environment bound to the active profile.
/// Binding is permission-checked by the platform as the caller: the caller
/// must own the connection or have it shared.
/// </summary>
public interface IConnectionReferenceService
{
    Task<IReadOnlyList<ConnectionReferenceRecord>> ListAsync(
        string? profileName, ConnectionReferenceListOptions options, CancellationToken ct);

    /// <exception cref="ArgumentException">No reference has that logical name.</exception>
    Task<ConnectionReferenceRecord> GetAsync(string? profileName, string logicalName, CancellationToken ct);

    /// <summary>Idempotent by logical name.</summary>
    Task<ConnectionReferenceOutcome> CreateAsync(
        string? profileName, ConnectionReferenceCreateOptions options, CancellationToken ct);

    /// <summary>
    /// Binds the reference to a connection and/or renames it. Unbinding is not
    /// possible: the platform ignores an attempt to clear the connection.
    /// </summary>
    Task<ConnectionReferenceOutcome> UpdateAsync(
        string? profileName, string logicalName, string? connectionId, string? displayName, CancellationToken ct);

    Task DeleteAsync(string? profileName, string logicalName, CancellationToken ct);
}
