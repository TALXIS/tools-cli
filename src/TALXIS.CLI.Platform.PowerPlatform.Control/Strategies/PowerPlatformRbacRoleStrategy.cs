using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;

public sealed class PowerPlatformRbacRoleStrategy : IPowerPlatformRoleAssignmentStrategy
{
    private readonly PowerPlatformRbacClient _client;

    public PowerPlatformRbacRoleStrategy(PowerPlatformRbacClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public bool SupportsPrincipalType(PowerPlatformPrincipalType principalType) => true;

    /// <inheritdoc />
    /// <remarks>
    /// Handles every role identifier except the synthetic
    /// <see cref="BapAdminApplicationRoleStrategy.AdminApplicationRoleValue"/>,
    /// which is owned exclusively by <see cref="BapAdminApplicationRoleStrategy"/>.
    /// </remarks>
    public bool CanHandle(PowerPlatformPrincipalType principalType, string roleNameOrId)
        => !string.Equals(roleNameOrId, BapAdminApplicationRoleStrategy.AdminApplicationRoleValue, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        CancellationToken ct)
    {
        var tenantRoles = await ListTenantAssignableRoleDefinitionsAsync(connection, credential, ct)
            .ConfigureAwait(false);
        var tenantRoleIds = tenantRoles.Select(r => r.RoleDefinitionId).ToHashSet();
        var roleNameById = tenantRoles.ToDictionary(r => r.RoleDefinitionId, r => r.RoleDefinitionName);

        var assignments = await _client.ListTenantRoleAssignmentsAsync(connection, credential, ct)
            .ConfigureAwait(false);

        return assignments
            .Where(a => a.PrincipalType == principal.PrincipalType && a.PrincipalObjectId == principal.ObjectId)
            .Where(a => tenantRoleIds.Contains(a.RoleDefinitionId))
            .Select(a => new PowerPlatformTenantRoleAssignment(
                RoleIdentifier: a.RoleDefinitionId.ToString(),
                RoleName: roleNameById[a.RoleDefinitionId],
                Scope: a.Scope,
                PrincipalType: a.PrincipalType,
                PrincipalObjectId: a.PrincipalObjectId,
                AssignmentId: a.RoleAssignmentId,
                CreatedOn: a.CreatedOn,
                ExpiresOn: a.ExpiresOn,
                IsSynthetic: false))
            .ToList();
    }

    public async Task AddAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        string roleNameOrId,
        CancellationToken ct)
    {
        var role = await ResolveTenantRoleAsync(connection, credential, roleNameOrId, ct).ConfigureAwait(false);
        await AddAsync(connection, credential, principal, role, ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        string roleNameOrId,
        CancellationToken ct)
    {
        var role = await ResolveTenantRoleAsync(connection, credential, roleNameOrId, ct).ConfigureAwait(false);
        await RemoveAsync(connection, credential, principal, role, ct).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<PowerPlatformRoleDefinition>> ListTenantAssignableRoleDefinitionsAsync(
        Connection connection,
        Credential credential,
        CancellationToken ct)
    {
        var roles = await _client.ListRoleDefinitionsAsync(connection, credential, ct).ConfigureAwait(false);
        return roles
            .Where(IsTenantAssignable)
            .OrderBy(r => r.RoleDefinitionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal async Task<PowerPlatformRoleDefinition> ResolveTenantRoleAsync(
        Connection connection,
        Credential credential,
        string roleNameOrId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleNameOrId);

        var roles = await ListTenantAssignableRoleDefinitionsAsync(connection, credential, ct).ConfigureAwait(false);
        var matches = TryCollectMatches(roles, roleNameOrId);

        if (matches.Count == 0)
            throw new TenantRoleNotFoundException(roleNameOrId);

        if (matches.Count > 1)
            throw new TenantRoleAmbiguousException(roleNameOrId, matches.Select(r => r.RoleDefinitionName));

        return matches[0];
    }

    internal async Task AddAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        PowerPlatformRoleDefinition role,
        CancellationToken ct)
    {
        var existingAssignments = await _client.ListTenantRoleAssignmentsAsync(connection, credential, ct)
            .ConfigureAwait(false);

        if (existingAssignments.Any(a =>
                a.PrincipalType == principal.PrincipalType
                && a.PrincipalObjectId == principal.ObjectId
                && a.RoleDefinitionId == role.RoleDefinitionId))
        {
            return;
        }

        _ = await _client.AddTenantRoleAssignmentAsync(
            connection,
            credential,
            principal.PrincipalType,
            principal.ObjectId,
            role.RoleDefinitionId,
            ct).ConfigureAwait(false);
    }

    internal async Task RemoveAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        PowerPlatformRoleDefinition role,
        CancellationToken ct)
    {
        var existingAssignments = await _client.ListTenantRoleAssignmentsAsync(connection, credential, ct)
            .ConfigureAwait(false);

        var matchingAssignments = existingAssignments
            .Where(a => a.PrincipalType == principal.PrincipalType
                && a.PrincipalObjectId == principal.ObjectId
                && a.RoleDefinitionId == role.RoleDefinitionId)
            .ToList();

        foreach (var assignment in matchingAssignments)
        {
            await _client.RemoveTenantRoleAssignmentAsync(connection, credential, assignment.RoleAssignmentId, ct)
                .ConfigureAwait(false);
        }
    }

    private static bool IsTenantAssignable(PowerPlatformRoleDefinition role)
        => role.AssignableScopes.Any(scope =>
            scope.Contains("/tenants/", StringComparison.OrdinalIgnoreCase));

    private static List<PowerPlatformRoleDefinition> TryCollectMatches(
        IReadOnlyList<PowerPlatformRoleDefinition> roles,
        string roleNameOrId)
    {
        if (Guid.TryParse(roleNameOrId, out var roleId))
        {
            return roles.Where(r => r.RoleDefinitionId == roleId).ToList();
        }

        return roles
            .Where(r => r.RoleDefinitionName.Equals(roleNameOrId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
