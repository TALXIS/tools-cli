namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Dataverse-plane operations over regular environment users
/// (<c>systemuser</c> rows whose <c>applicationid</c> is null).
/// </summary>
public interface IDataverseUserService
{
    /// <summary>
    /// Lists Dataverse environment users filtered by enabled state.
    /// </summary>
    Task<IReadOnlyList<DataverseUserRecord>> ListAsync(
        string? profileName,
        DataverseSecurityPrincipalStateFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Resolves a single Dataverse environment user by system-user GUID or UPN.
    /// Returns <c>null</c> when no record matches. Throws
    /// <see cref="DataverseAmbiguousMatchException"/> when the friendly
    /// identifier matches more than one row.
    /// </summary>
    Task<DataverseUserRecord?> GetAsync(
        string? profileName,
        string userIdOrUpn,
        CancellationToken ct);

    /// <summary>
    /// Enables or disables a Dataverse environment user resolved from a GUID or
    /// UPN. Throws <see cref="DataverseAmbiguousMatchException"/> when the
    /// friendly identifier is ambiguous.
    /// </summary>
    Task UpdateEnabledStateAsync(
        string? profileName,
        string userIdOrUpn,
        bool enabled,
        CancellationToken ct);

    /// <summary>
    /// Lists security roles assigned to the resolved Dataverse environment
    /// user. Throws <see cref="DataverseAmbiguousMatchException"/> when the
    /// friendly identifier is ambiguous.
    /// </summary>
    Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(
        string? profileName,
        string userIdOrUpn,
        CancellationToken ct);

    /// <summary>
    /// Assigns a Dataverse security role to the resolved environment user.
    /// Both the user lookup and the role lookup accept either GUIDs or friendly
    /// identifiers and throw <see cref="DataverseAmbiguousMatchException"/>
    /// when a friendly identifier matches multiple rows.
    /// </summary>
    Task AddRoleAsync(
        string? profileName,
        string userIdOrUpn,
        string roleNameOrGuid,
        CancellationToken ct);

    /// <summary>
    /// Removes a Dataverse security role from the resolved environment user.
    /// Both the user lookup and the role lookup accept either GUIDs or friendly
    /// identifiers and throw <see cref="DataverseAmbiguousMatchException"/>
    /// when a friendly identifier matches multiple rows.
    /// </summary>
    Task RemoveRoleAsync(
        string? profileName,
        string userIdOrUpn,
        string roleNameOrGuid,
        CancellationToken ct);
}
