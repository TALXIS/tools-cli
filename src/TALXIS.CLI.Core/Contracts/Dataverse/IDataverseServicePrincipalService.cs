namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Dataverse-plane operations over service principals
/// (<c>systemuser</c> rows whose <c>applicationid</c> is populated).
/// </summary>
public interface IDataverseServicePrincipalService
{
    /// <summary>
    /// Lists Dataverse service principals filtered by enabled state.
    /// </summary>
    Task<IReadOnlyList<DataverseServicePrincipalRecord>> ListAsync(
        string? profileName,
        DataverseSecurityPrincipalStateFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Resolves a single Dataverse service principal by system-user GUID or
    /// Entra application (client) ID. Returns <c>null</c> when no record
    /// matches. Throws <see cref="DataverseAmbiguousMatchException"/> when a
    /// GUID could legitimately match multiple service-principal records.
    /// </summary>
    Task<DataverseServicePrincipalRecord?> GetAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct);

    /// <summary>
    /// Creates a Dataverse service principal directly in the environment and
    /// optionally assigns initial roles. When no business unit is supplied, the
    /// current caller's business unit is used.
    /// </summary>
    Task<DataverseServicePrincipalRecord> CreateAsync(
        string? profileName,
        DataverseServicePrincipalCreateOptions options,
        CancellationToken ct);

    /// <summary>
    /// Enables or disables a Dataverse service principal resolved from a system
    /// user GUID or client ID. Throws <see cref="DataverseAmbiguousMatchException"/>
    /// when the identifier is ambiguous.
    /// </summary>
    Task UpdateEnabledStateAsync(
        string? profileName,
        string clientIdOrGuid,
        bool enabled,
        CancellationToken ct);

    /// <summary>
    /// Hard-deletes a Dataverse service principal. Dataverse only allows this
    /// once the service principal is already disabled; this service validates the
    /// precondition before issuing the delete.
    /// </summary>
    Task DeleteAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct);

    /// <summary>
    /// Lists security roles assigned to the resolved Dataverse application
    /// user. Throws <see cref="DataverseAmbiguousMatchException"/> when the
    /// identifier is ambiguous.
    /// </summary>
    Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct);

    /// <summary>
    /// Assigns a Dataverse security role to the resolved service principal.
    /// Both the service-principal lookup and the role lookup accept either GUIDs
    /// or friendly identifiers and throw
    /// <see cref="DataverseAmbiguousMatchException"/> when a friendly
    /// identifier matches multiple rows.
    /// </summary>
    Task AddRoleAsync(
        string? profileName,
        string clientIdOrGuid,
        string roleNameOrGuid,
        CancellationToken ct);

    /// <summary>
    /// Removes a Dataverse security role from the resolved service principal.
    /// Both the service-principal lookup and the role lookup accept either GUIDs
    /// or friendly identifiers and throw
    /// <see cref="DataverseAmbiguousMatchException"/> when a friendly
    /// identifier matches multiple rows.
    /// </summary>
    Task RemoveRoleAsync(
        string? profileName,
        string clientIdOrGuid,
        string roleNameOrGuid,
        CancellationToken ct);
}
