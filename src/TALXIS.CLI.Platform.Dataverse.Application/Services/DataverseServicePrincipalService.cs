using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseServicePrincipalService : IDataverseServicePrincipalService
{
    public async Task<IReadOnlyList<DataverseServicePrincipalRecord>> ListAsync(
        string? profileName,
        DataverseSecurityPrincipalStateFilter filter,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListServicePrincipalsAsync(conn.Client, filter, ct).ConfigureAwait(false);
    }

    public async Task<DataverseServicePrincipalRecord?> GetAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.GetServicePrincipalAsync(conn.Client, clientIdOrGuid, ct).ConfigureAwait(false);
    }

    public async Task<DataverseServicePrincipalRecord> CreateAsync(
        string? profileName,
        DataverseServicePrincipalCreateOptions options,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.CreateServicePrincipalAsync(conn.Client, options, ct).ConfigureAwait(false);
    }

    public async Task UpdateEnabledStateAsync(
        string? profileName,
        string clientIdOrGuid,
        bool enabled,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.UpdateServicePrincipalEnabledStateAsync(conn.Client, clientIdOrGuid, enabled, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.DeleteServicePrincipalAsync(conn.Client, clientIdOrGuid, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(
        string? profileName,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListServicePrincipalRolesAsync(conn.Client, clientIdOrGuid, ct).ConfigureAwait(false);
    }

    public async Task AddRoleAsync(
        string? profileName,
        string clientIdOrGuid,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.AddServicePrincipalRoleAsync(conn.Client, clientIdOrGuid, roleNameOrGuid, ct).ConfigureAwait(false);
    }

    public async Task RemoveRoleAsync(
        string? profileName,
        string clientIdOrGuid,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await DataverseSecurityPrincipalManager.RemoveServicePrincipalRoleAsync(conn.Client, clientIdOrGuid, roleNameOrGuid, ct).ConfigureAwait(false);
    }
}
