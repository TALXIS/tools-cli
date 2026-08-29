using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Governance;

/// <summary>
/// Thin wrapper over <see cref="PowerPlatformRbacClient"/>'s dedicated
/// environment-group role-assignment methods
/// (<c>authorization/environmentGroups/{id}/roleAssignments</c>). Unlike
/// tenant-scoped assignments, environment-group assignments are NOT
/// reachable via the generic <c>authorization/roleAssignments?scope=</c>
/// query filter - Microsoft's REST API reference confirms a dedicated,
/// path-routed resource for this scope instead, with no <c>scope</c> field
/// in the request body (the environment group id in the URL implies it).
/// </summary>
public sealed class PowerPlatformEnvironmentGroupRoleClient : IPowerPlatformEnvironmentGroupRoleClient
{
    private readonly PowerPlatformRbacClient _rbac;

    public PowerPlatformEnvironmentGroupRoleClient(PowerPlatformRbacClient rbac)
    {
        _rbac = rbac ?? throw new ArgumentNullException(nameof(rbac));
    }

    public async Task<IReadOnlyList<PowerPlatformEnvironmentGroupRoleAssignment>> ListAsync(
        Connection connection, Credential credential, Guid environmentGroupId, CancellationToken ct)
    {
        var assignments = await _rbac.ListEnvironmentGroupRoleAssignmentsAsync(
            connection, credential, environmentGroupId, ct).ConfigureAwait(false);

        return assignments.Select(a => ToContract(a, environmentGroupId)).ToList();
    }

    public async Task<PowerPlatformEnvironmentGroupRoleAssignment> AddAsync(
        Connection connection, Credential credential, Guid environmentGroupId,
        PowerPlatformPrincipalType principalType, Guid principalObjectId, Guid roleDefinitionId, CancellationToken ct)
    {
        var assignment = await _rbac.AddEnvironmentGroupRoleAssignmentAsync(
            connection, credential, environmentGroupId, principalType, principalObjectId, roleDefinitionId, ct)
            .ConfigureAwait(false);

        if (assignment is null)
        {
            throw new InvalidOperationException(
                $"Power Platform did not return a role assignment payload after adding the role to environment group '{environmentGroupId}'.");
        }

        return ToContract(assignment, environmentGroupId);
    }

    public Task RemoveAsync(
        Connection connection, Credential credential, Guid environmentGroupId, string roleAssignmentId, CancellationToken ct)
        => _rbac.RemoveEnvironmentGroupRoleAssignmentAsync(connection, credential, environmentGroupId, roleAssignmentId, ct);

    private static PowerPlatformEnvironmentGroupRoleAssignment ToContract(PowerPlatformRbacRoleAssignment assignment, Guid environmentGroupId)
        => new(
            assignment.RoleAssignmentId,
            environmentGroupId,
            assignment.PrincipalType,
            assignment.PrincipalObjectId,
            assignment.RoleDefinitionId,
            assignment.CreatedOn,
            assignment.ExpiresOn);
}
