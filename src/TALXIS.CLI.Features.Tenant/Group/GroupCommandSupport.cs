using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Tenant;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Features.Tenant.Group;

/// <summary>
/// Command support for <c>txc tenant group role ...</c>. Unlike the user/app
/// equivalents, this deliberately never calls Microsoft Graph to resolve a
/// group by display name — that would require the <c>Group.Read.All</c>
/// permission, which is not pre-consented for this CLI's Entra app
/// registration in most tenants. Groups are always identified by their raw
/// Entra object id (GUID) instead, matching how <c>pac admin assign-group</c>
/// requires callers to already know the group's object id.
/// </summary>
internal static class GroupCommandSupport
{
    public static async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListRolesAsync(
        string? profile,
        string group,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        return await resolver.ListAssignmentsAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.Group,
            group,
            ct).ConfigureAwait(false);
    }

    public static async Task AddRoleAsync(
        string? profile,
        string group,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.AddAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.Group,
            group,
            role,
            ct).ConfigureAwait(false);
    }

    public static async Task RemoveRoleAsync(
        string? profile,
        string group,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.RemoveAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.Group,
            group,
            role,
            ct).ConfigureAwait(false);
    }
}
