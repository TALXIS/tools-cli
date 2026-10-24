using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;

/// <summary>
/// Implements the synthetic <c>admin-application</c> tenant role by registering/deregistering
/// the application with the Power Platform Admin (BAP) API's <c>adminApplications</c> endpoint,
/// the mechanism Microsoft documents at
/// <see href="https://learn.microsoft.com/en-us/power-platform/admin/powerplatform-api-create-service-principal">
/// Create a service principal to create and manage environments and other resources for Power
/// Platform</see>. Unlike every other tenant role, this is not a Power Platform RBAC role
/// assignment - <see cref="PowerPlatformTenantRoleAssignment.IsSynthetic"/> is always
/// <see langword="true"/> for assignments produced by this strategy, so callers can tell it
/// apart from real RBAC assignments in <c>security service-principal role list</c> output.
/// </summary>
public sealed class BapAdminApplicationRoleStrategy : IPowerPlatformRoleAssignmentStrategy
{
    public const string AdminApplicationRoleValue = "admin-application";

    private readonly BapAdminApiClient _bap;

    public BapAdminApplicationRoleStrategy(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
        : this(new BapAdminApiClient(tokens, httpFactory))
    {
    }

    internal BapAdminApplicationRoleStrategy(BapAdminApiClient bap)
    {
        _bap = bap ?? throw new ArgumentNullException(nameof(bap));
    }

    /// <inheritdoc />
    public bool SupportsPrincipalType(PowerPlatformPrincipalType principalType)
        => principalType == PowerPlatformPrincipalType.ApplicationUser;

    /// <inheritdoc />
    public bool CanHandle(PowerPlatformPrincipalType principalType, string roleNameOrId)
        => principalType == PowerPlatformPrincipalType.ApplicationUser
            && string.Equals(roleNameOrId, AdminApplicationRoleValue, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        CancellationToken ct)
    {
        ValidatePrincipal(principal);

        var registrations = await _bap.ListAdminApplicationsAsync(connection, credential, ct).ConfigureAwait(false);
        if (!registrations.Any(a => a.ApplicationId == principal.ApplicationId))
            return Array.Empty<PowerPlatformTenantRoleAssignment>();

        return new[]
        {
            new PowerPlatformTenantRoleAssignment(
                RoleIdentifier: AdminApplicationRoleValue,
                RoleName: AdminApplicationRoleValue,
                Scope: PowerPlatformRbacClient.BuildTenantScope(connection),
                PrincipalType: principal.PrincipalType,
                PrincipalObjectId: principal.ObjectId,
                AssignmentId: null,
                CreatedOn: null,
                ExpiresOn: null,
                IsSynthetic: true),
        };
    }

    public async Task AddAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        string roleNameOrId,
        CancellationToken ct)
    {
        ValidatePrincipal(principal);
        ValidateRole(roleNameOrId);

        var registrations = await _bap.ListAdminApplicationsAsync(connection, credential, ct).ConfigureAwait(false);
        if (registrations.Any(a => a.ApplicationId == principal.ApplicationId))
            return;

        await _bap.RegisterAdminApplicationAsync(connection, credential, principal.ApplicationId!.Value, ct)
            .ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        Connection connection,
        Credential credential,
        PowerPlatformRolePrincipalReference principal,
        string roleNameOrId,
        CancellationToken ct)
    {
        ValidatePrincipal(principal);
        ValidateRole(roleNameOrId);

        var registrations = await _bap.ListAdminApplicationsAsync(connection, credential, ct).ConfigureAwait(false);
        if (!registrations.Any(a => a.ApplicationId == principal.ApplicationId))
            return;

        await _bap.UnregisterAdminApplicationAsync(connection, credential, principal.ApplicationId!.Value, ct)
            .ConfigureAwait(false);
    }

    private static void ValidatePrincipal(PowerPlatformRolePrincipalReference principal)
    {
        if (principal.PrincipalType != PowerPlatformPrincipalType.ApplicationUser)
        {
            throw new ArgumentException(
                $"The '{AdminApplicationRoleValue}' role is only valid for application principals.",
                nameof(principal));
        }

        if (!principal.ApplicationId.HasValue)
        {
            throw new ArgumentException(
                $"Application principals must include the client/application id when using '{AdminApplicationRoleValue}'.",
                nameof(principal));
        }
    }

    private static void ValidateRole(string roleNameOrId)
    {
        if (!string.Equals(roleNameOrId, AdminApplicationRoleValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The BAP admin-application strategy only supports the synthetic role value '{AdminApplicationRoleValue}'.",
                nameof(roleNameOrId));
        }
    }
}
