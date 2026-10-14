using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

/// <summary>
/// Command support for <c>txc governance environment-group role ...</c>.
/// Reuses <see cref="SecurityRoleResolver"/>'s Microsoft Graph-backed
/// principal resolution (the same logic <c>txc security</c> uses for
/// user/service-principal/group lookups) so this command surface never
/// duplicates Graph query/paging code. Role definitions are resolved
/// against the tenant's full RBAC role catalog because environment-group
/// role assignments use the built-in RBAC roles (Owner/Contributor/Reader/
/// RBAC Administrator) — the same catalog tenant-scoped assignments use —
/// rather than a separate environment-group-only role set.
/// </summary>
internal static class EnvironmentGroupRoleCommandSupport
{
    public static async Task<IReadOnlyList<PowerPlatformEnvironmentGroupRoleAssignment>> ListRolesAsync(
        string? profile,
        string environmentGroup,
        CancellationToken ct)
    {
        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var group = await EnvironmentGroupCommandSupport
            .ResolveAsync(context.Connection, context.Credential, environmentGroup, ct)
            .ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupRoleClient>();
        return await client.ListAsync(context.Connection, context.Credential, group.Id, ct).ConfigureAwait(false);
    }

    public static async Task<PowerPlatformEnvironmentGroupRoleAssignment> AddRoleAsync(
        string? profile,
        string environmentGroup,
        PowerPlatformPrincipalType principalType,
        string principal,
        string roleNameOrId,
        CancellationToken ct)
    {
        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var group = await EnvironmentGroupCommandSupport
            .ResolveAsync(context.Connection, context.Credential, environmentGroup, ct)
            .ConfigureAwait(false);

        var principalRef = await ResolvePrincipalAsync(context, principalType, principal, ct).ConfigureAwait(false);
        var role = await ResolveRoleAsync(context, roleNameOrId, ct).ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupRoleClient>();

        // Idempotent: if the principal already holds this role on this group, do nothing.
        var existing = await client.ListAsync(context.Connection, context.Credential, group.Id, ct).ConfigureAwait(false);
        var already = existing.FirstOrDefault(a =>
            a.PrincipalType == principalRef.PrincipalType
            && a.PrincipalObjectId == principalRef.ObjectId
            && a.RoleDefinitionId == role.RoleDefinitionId);
        if (already is not null)
            return already;

        return await client.AddAsync(
            context.Connection, context.Credential, group.Id,
            principalRef.PrincipalType, principalRef.ObjectId, role.RoleDefinitionId, ct).ConfigureAwait(false);
    }

    public static async Task RemoveRoleAsync(
        string? profile,
        string environmentGroup,
        PowerPlatformPrincipalType principalType,
        string principal,
        string roleNameOrId,
        CancellationToken ct)
    {
        var context = await EnvironmentGroupCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var group = await EnvironmentGroupCommandSupport
            .ResolveAsync(context.Connection, context.Credential, environmentGroup, ct)
            .ConfigureAwait(false);

        var principalRef = await ResolvePrincipalAsync(context, principalType, principal, ct).ConfigureAwait(false);
        var role = await ResolveRoleAsync(context, roleNameOrId, ct).ConfigureAwait(false);

        var client = TxcServices.Get<IPowerPlatformEnvironmentGroupRoleClient>();
        var existing = await client.ListAsync(context.Connection, context.Credential, group.Id, ct).ConfigureAwait(false);
        var matches = existing.Where(a =>
                a.PrincipalType == principalRef.PrincipalType
                && a.PrincipalObjectId == principalRef.ObjectId
                && a.RoleDefinitionId == role.RoleDefinitionId)
            .ToList();

        foreach (var match in matches)
        {
            await client.RemoveAsync(context.Connection, context.Credential, group.Id, match.RoleAssignmentId, ct)
                .ConfigureAwait(false);
        }
    }

    private static Task<PowerPlatformRolePrincipalReference> ResolvePrincipalAsync(
        Core.Model.ResolvedProfileContext context,
        PowerPlatformPrincipalType principalType,
        string principal,
        CancellationToken ct)
        => TxcServices.Get<SecurityRoleResolver>()
            .ResolvePrincipalAsync(context.Connection, context.Credential, principalType, principal, ct);

    private static async Task<PowerPlatformRoleDefinition> ResolveRoleAsync(
        Core.Model.ResolvedProfileContext context,
        string roleNameOrId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleNameOrId);

        var rbac = TxcServices.Get<PowerPlatformRbacClient>();
        var roles = await rbac.ListRoleDefinitionsAsync(context.Connection, context.Credential, ct).ConfigureAwait(false);

        var matches = Guid.TryParse(roleNameOrId, out var roleId)
            ? roles.Where(r => r.RoleDefinitionId == roleId).ToList()
            : roles.Where(r => r.RoleDefinitionName.Equals(roleNameOrId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
            throw new TenantRoleNotFoundException(roleNameOrId);

        if (matches.Count > 1)
            throw new TenantRoleAmbiguousException(roleNameOrId, matches.Select(r => r.RoleDefinitionName));

        return matches[0];
    }
}
