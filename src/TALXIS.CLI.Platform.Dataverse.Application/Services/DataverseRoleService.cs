using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseRoleService : IDataverseRoleService
{
    public async Task<IReadOnlyList<DataverseRoleRecord>> ListAsync(
        string? profileName,
        string? filter,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.ListRolesAsync(conn.Client, filter, ct).ConfigureAwait(false);
    }

    public async Task<DataverseRoleRecord?> GetAsync(
        string? profileName,
        string nameOrGuid,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await DataverseSecurityPrincipalManager.GetRoleAsync(conn.Client, nameOrGuid, ct).ConfigureAwait(false);
    }
}
