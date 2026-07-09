namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Dataverse-plane operations over application users
/// (<c>systemuser</c> rows whose <c>applicationid</c> is populated).
/// </summary>
public interface IDataverseAppUserService
{
    /// <summary>
    /// Lists Dataverse application users filtered by enabled state.
    /// </summary>
    Task<IReadOnlyList<DataverseAppUserRecord>> ListAsync(
        string? profileName,
        DataverseSecurityPrincipalStateFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Resolves a single Dataverse application user by system-user GUID or
    /// Entra application (client) ID. Returns <c>null</c> when no record
    /// matches. Throws <see cref="DataverseAmbiguousMatchException"/> when a
    /// GUID could legitimately match multiple application-user records.
    /// </summary>
    Task<DataverseAppUserRecord?> GetAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct);

    /// <summary>
    /// Creates a Dataverse application user directly in the environment and
    /// optionally assigns initial roles. When no business unit is supplied, the
    /// current caller's business unit is used.
    /// </summary>
    Task<DataverseAppUserRecord> CreateAsync(
        string? profileName,
        DataverseAppUserCreateOptions options,
        CancellationToken ct);

    /// <summary>
    /// Enables or disables a Dataverse application user resolved from a system
    /// user GUID or client ID. Throws <see cref="DataverseAmbiguousMatchException"/>
    /// when the identifier is ambiguous.
    /// </summary>
    Task UpdateEnabledStateAsync(
        string? profileName,
        string clientIdOrGuid,
        bool enabled,
        CancellationToken ct);

    /// <summary>
    /// Hard-deletes a Dataverse application user. Dataverse only allows this
    /// once the application user is already disabled; this service validates the
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
    /// Assigns a Dataverse security role to the resolved application user.
    /// Both the application-user lookup and the role lookup accept either GUIDs
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
    /// Removes a Dataverse security role from the resolved application user.
    /// Both the application-user lookup and the role lookup accept either GUIDs
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
