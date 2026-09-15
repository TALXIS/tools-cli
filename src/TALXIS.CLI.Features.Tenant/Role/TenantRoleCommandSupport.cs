using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Tenant;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Features.Tenant.Role;

internal static class TenantRoleCommandSupport
{
    public static async Task<IReadOnlyList<PowerPlatformRoleDefinition>> ListRolesAsync(
        string? profile,
        string? filter,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        return await resolver.ListTenantRolesAsync(context.Connection, context.Credential, filter, ct).ConfigureAwait(false);
    }

    public static async Task<PowerPlatformRoleDefinition> GetRoleAsync(
        string? profile,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        return await resolver.GetTenantRoleAsync(context.Connection, context.Credential, role, ct).ConfigureAwait(false);
    }
}
