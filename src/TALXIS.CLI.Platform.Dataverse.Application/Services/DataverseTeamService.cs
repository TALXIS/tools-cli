using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseTeamService : IDataverseTeamService
{
    public async Task<IReadOnlyList<DataverseTeamRecord>> ListAsync(
        string? profileName,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListTeamsAsync(conn.Client, ct).ConfigureAwait(false);
    }

    public async Task<DataverseTeamRecord?> GetAsync(
        string? profileName,
        string nameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.GetTeamAsync(conn.Client, nameOrGuid, ct).ConfigureAwait(false);
    }

    public async Task<DataverseTeamRecord> CreateAsync(
        string? profileName,
        DataverseTeamCreateOptions options,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.CreateTeamAsync(conn.Client, options, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string? profileName,
        string nameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.DeleteTeamAsync(conn.Client, nameOrGuid, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DataverseUserRecord>> ListMembersAsync(
        string? profileName,
        string teamIdOrName,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListTeamMembersAsync(conn.Client, teamIdOrName, ct).ConfigureAwait(false);
    }

    public async Task AddMemberAsync(
        string? profileName,
        string teamIdOrName,
        string userIdOrUpn,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.AddTeamMemberAsync(conn.Client, teamIdOrName, userIdOrUpn, ct).ConfigureAwait(false);
    }

    public async Task RemoveMemberAsync(
        string? profileName,
        string teamIdOrName,
        string userIdOrUpn,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.RemoveTeamMemberAsync(conn.Client, teamIdOrName, userIdOrUpn, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(
        string? profileName,
        string teamIdOrName,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListTeamRolesAsync(conn.Client, teamIdOrName, ct).ConfigureAwait(false);
    }

    public async Task AddRoleAsync(
        string? profileName,
        string teamIdOrName,
        string roleNameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.AddTeamRoleAsync(conn.Client, teamIdOrName, roleNameOrGuid, ct).ConfigureAwait(false);
    }

    public async Task RemoveRoleAsync(
        string? profileName,
        string teamIdOrName,
        string roleNameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.RemoveTeamRoleAsync(conn.Client, teamIdOrName, roleNameOrGuid, ct).ConfigureAwait(false);
    }
}
