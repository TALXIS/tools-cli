using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseUserService : IDataverseUserService
{
    public async Task<IReadOnlyList<DataverseUserRecord>> ListAsync(
        string? profileName,
        DataverseSecurityPrincipalStateFilter filter,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListRegularUsersAsync(conn.Client, filter, ct).ConfigureAwait(false);
    }

    public async Task<DataverseUserRecord?> GetAsync(
        string? profileName,
        string userIdOrUpn,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.GetRegularUserAsync(conn.Client, userIdOrUpn, ct).ConfigureAwait(false);
    }

    public async Task UpdateEnabledStateAsync(
        string? profileName,
        string userIdOrUpn,
        bool enabled,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.UpdateRegularUserEnabledStateAsync(conn.Client, userIdOrUpn, enabled, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(
        string? profileName,
        string userIdOrUpn,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListRegularUserRolesAsync(conn.Client, userIdOrUpn, ct).ConfigureAwait(false);
    }

    public async Task AddRoleAsync(
        string? profileName,
        string userIdOrUpn,
        string roleNameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.AddRegularUserRoleAsync(conn.Client, userIdOrUpn, roleNameOrGuid, ct).ConfigureAwait(false);
    }

    public async Task RemoveRoleAsync(
        string? profileName,
        string userIdOrUpn,
        string roleNameOrGuid,
        CancellationToken ct,
        Guid? environmentId = null)
    {
        var context = await DataverseScopedCommandSupport.ResolveContextAsync(profileName, environmentId, ct).ConfigureAwait(false);
        using var conn = await DataverseCommandBridge.ConnectAsync(context, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.RemoveRegularUserRoleAsync(conn.Client, userIdOrUpn, roleNameOrGuid, ct).ConfigureAwait(false);
    }
}
