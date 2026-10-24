namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Dataverse-plane operations over environment teams.
/// </summary>
public interface IDataverseTeamService
{
    /// <summary>
    /// Lists Dataverse teams in the current environment.
    /// </summary>
    Task<IReadOnlyList<DataverseTeamRecord>> ListAsync(
        string? profileName,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Resolves a single Dataverse team by team GUID or exact team name.
    /// Returns <c>null</c> when no record matches. Throws
    /// <see cref="DataverseAmbiguousMatchException"/> when the friendly name
    /// matches more than one team.
    /// </summary>
    Task<DataverseTeamRecord?> GetAsync(
        string? profileName,
        string nameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Creates a Dataverse team. When no business unit is supplied, the current
    /// caller's business unit is used. Azure AD-backed team types require
    /// <see cref="DataverseTeamCreateOptions.AadObjectId"/>.
    /// </summary>
    Task<DataverseTeamRecord> CreateAsync(
        string? profileName,
        DataverseTeamCreateOptions options,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Deletes a Dataverse team resolved from a GUID or exact team name.
    /// Throws <see cref="DataverseAmbiguousMatchException"/> when the name is
    /// ambiguous.
    /// </summary>
    Task DeleteAsync(
        string? profileName,
        string nameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Lists Dataverse users that are direct members of an owner or access
    /// team. For Azure AD-backed security-group and office-group teams, the
    /// returned membership still reflects Dataverse state, but add/remove
    /// operations are rejected because membership is managed in Entra ID.
    /// </summary>
    Task<IReadOnlyList<DataverseUserRecord>> ListMembersAsync(
        string? profileName,
        string teamIdOrName,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Adds a Dataverse environment user to an owner or access team. Throws a
    /// clear <see cref="InvalidOperationException"/> for Azure AD-backed team
    /// types because their membership is managed in Entra ID.
    /// </summary>
    Task AddMemberAsync(
        string? profileName,
        string teamIdOrName,
        string userIdOrUpn,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Removes a Dataverse environment user from an owner or access team.
    /// Throws a clear <see cref="InvalidOperationException"/> for Azure
    /// AD-backed team types because their membership is managed in Entra ID.
    /// </summary>
    Task RemoveMemberAsync(
        string? profileName,
        string teamIdOrName,
        string userIdOrUpn,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Lists security roles assigned to the resolved Dataverse team. Throws
    /// <see cref="DataverseAmbiguousMatchException"/> when the team name is
    /// ambiguous.
    /// </summary>
    Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(
        string? profileName,
        string teamIdOrName,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Assigns a Dataverse security role to the resolved team. Both the team
    /// lookup and the role lookup accept either GUIDs or friendly identifiers
    /// and throw <see cref="DataverseAmbiguousMatchException"/> when a
    /// friendly identifier matches multiple rows.
    /// </summary>
    Task AddRoleAsync(
        string? profileName,
        string teamIdOrName,
        string roleNameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null);

    /// <summary>
    /// Removes a Dataverse security role from the resolved team. Both the team
    /// lookup and the role lookup accept either GUIDs or friendly identifiers
    /// and throw <see cref="DataverseAmbiguousMatchException"/> when a
    /// friendly identifier matches multiple rows.
    /// </summary>
    Task RemoveRoleAsync(
        string? profileName,
        string teamIdOrName,
        string roleNameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null);
}
