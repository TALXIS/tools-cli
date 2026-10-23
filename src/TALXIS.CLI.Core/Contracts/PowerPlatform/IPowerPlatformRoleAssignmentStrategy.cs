using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Core.Contracts.PowerPlatform;

/// <summary>
/// Tenant-scoped Power Platform RBAC principal kinds.
/// </summary>
public enum PowerPlatformPrincipalType
{
    User = 0,
    Group = 1,

    /// <summary>
    /// A service principal (Entra application), tenant-scoped for RBAC.
    /// The member name is intentionally kept as <c>ApplicationUser</c>
    /// (not renamed to <c>ServicePrincipal</c>) because its
    /// <see cref="object.ToString"/> value is sent as the literal
    /// <c>principalType</c> value to the real Power Platform Admin RBAC API
    /// and parsed back from that API's responses via
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> — this is
    /// an external wire-format contract, not just an internal name.
    /// </summary>
    ApplicationUser = 2,
}

/// <summary>
/// Resolved principal identifiers used by tenant-scoped role assignment flows.
/// Application principals carry both the service principal object id (for
/// Power Platform RBAC) and the application/client id (for the legacy BAP
/// admin-application registration).
/// </summary>
public sealed record PowerPlatformRolePrincipalReference(
    PowerPlatformPrincipalType PrincipalType,
    Guid ObjectId,
    Guid? ApplicationId = null,
    string? DisplayName = null,
    string? UserPrincipalName = null);

/// <summary>
/// Resolved Power Platform tenant role definition metadata.
/// </summary>
public sealed record PowerPlatformRoleDefinition(
    Guid RoleDefinitionId,
    string RoleDefinitionName,
    string? Description,
    IReadOnlyList<string> AssignableScopes);

/// <summary>
/// A tenant-scoped role assignment projected into a strategy-neutral shape so
/// callers can merge native PP-RBAC assignments with synthetic roles like
/// <c>admin-application</c>.
/// </summary>
public sealed record PowerPlatformTenantRoleAssignment(
    string RoleIdentifier,
    string RoleName,
    string Scope,
    PowerPlatformPrincipalType PrincipalType,
    Guid PrincipalObjectId,
    string? AssignmentId,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ExpiresOn,
    bool IsSynthetic);

/// <summary>
/// Strategy abstraction for manipulating tenant-scoped role assignments.
/// Concrete implementations handle either native Power Platform RBAC roles or
/// synthetic/legacy role concepts such as <c>admin-application</c>.
/// <see cref="TALXIS.CLI.Platform.PowerPlatform.Control.TenantRoleResolver"/> uses
/// <see cref="SupportsPrincipalType"/>/<see cref="CanHandle"/> to route work to
/// the correct strategy instance without hardcoding per-strategy checks, so
/// adding a new strategy (e.g. a future synthetic role) requires only a new
/// implementation of this interface, not resolver changes.
/// </summary>
public interface IPowerPlatformRoleAssignmentStrategy
{
    /// <summary>
    /// Whether this strategy participates in enumerating existing assignments
    /// for the given principal type (used to fan <c>ListAssignmentsAsync</c>
    /// out across every applicable strategy).
    /// </summary>
    bool SupportsPrincipalType(PowerPlatformPrincipalType principalType);

    /// <summary>
    /// Whether this strategy exclusively owns Add/Remove mutation dispatch for
    /// the given principal type + role identifier combination. Exactly one
    /// registered strategy is expected to return <see langword="true"/> for
    /// any valid combination; when none does, the role/principal-type pairing
    /// is invalid.
    /// </summary>
    bool CanHandle(PowerPlatformPrincipalType principalType, string roleNameOrId);

    Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        CancellationToken ct);

    Task AddAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        string roleNameOrId,
        CancellationToken ct);

    Task RemoveAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        string roleNameOrId,
        CancellationToken ct);
}
