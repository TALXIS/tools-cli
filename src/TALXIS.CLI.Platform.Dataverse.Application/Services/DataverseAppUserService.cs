using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseAppUserService : IDataverseAppUserService
{
    public async Task<IReadOnlyList<DataverseAppUserRecord>> ListAsync(
        string? profileName,
        DataverseSecurityPrincipalStateFilter filter,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListApplicationUsersAsync(conn.Client, filter, ct).ConfigureAwait(false);
    }

    public async Task<DataverseAppUserRecord?> GetAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.GetApplicationUserAsync(conn.Client, clientIdOrGuid, ct).ConfigureAwait(false);
    }

    public async Task<DataverseAppUserRecord> CreateAsync(
        string? profileName,
        DataverseAppUserCreateOptions options,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.CreateApplicationUserAsync(conn.Client, options, ct).ConfigureAwait(false);
    }

    public async Task UpdateEnabledStateAsync(
        string? profileName,
        string clientIdOrGuid,
        bool enabled,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.UpdateApplicationUserEnabledStateAsync(conn.Client, clientIdOrGuid, enabled, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.DeleteApplicationUserAsync(conn.Client, clientIdOrGuid, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListApplicationUserRolesAsync(conn.Client, clientIdOrGuid, ct).ConfigureAwait(false);
    }

    public async Task AddRoleAsync(
        string? profileName,
        string clientIdOrGuid,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.AddApplicationUserRoleAsync(conn.Client, clientIdOrGuid, roleNameOrGuid, ct).ConfigureAwait(false);
    }

    public async Task RemoveRoleAsync(
        string? profileName,
        string clientIdOrGuid,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.RemoveApplicationUserRoleAsync(conn.Client, clientIdOrGuid, roleNameOrGuid, ct).ConfigureAwait(false);
    }
}
