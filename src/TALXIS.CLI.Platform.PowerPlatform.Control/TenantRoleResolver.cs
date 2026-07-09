using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

#pragma warning disable RS0030 // Domain-specific validation exceptions are intentional here.
public sealed class TenantRoleNotFoundException : ArgumentException
{
    public TenantRoleNotFoundException(string roleNameOrId)
        : base($"Tenant role '{roleNameOrId}' was not found.", nameof(roleNameOrId))
    {
        RoleNameOrId = roleNameOrId;
    }

    public string RoleNameOrId { get; }
}

public sealed class TenantRoleAmbiguousException : ArgumentException
{
    public TenantRoleAmbiguousException(string roleNameOrId, IEnumerable<string> candidateNames)
        : base(
            $"Tenant role '{roleNameOrId}' is ambiguous. Matches: {string.Join(", ", candidateNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}.",
            nameof(roleNameOrId))
    {
        RoleNameOrId = roleNameOrId;
        CandidateNames = candidateNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string RoleNameOrId { get; }

    public IReadOnlyList<string> CandidateNames { get; }
}

public sealed class TenantPrincipalNotFoundException : ArgumentException
{
    public TenantPrincipalNotFoundException(PowerPlatformPrincipalType principalType, string principalValue)
        : base($"{principalType} '{principalValue}' was not found in Microsoft Graph.", nameof(principalValue))
    {
        PrincipalType = principalType;
        PrincipalValue = principalValue;
    }

    public PowerPlatformPrincipalType PrincipalType { get; }

    public string PrincipalValue { get; }
}

public sealed class TenantPrincipalAmbiguousException : ArgumentException
{
    public TenantPrincipalAmbiguousException(PowerPlatformPrincipalType principalType, string principalValue, IEnumerable<string> candidates)
        : base(
            $"{principalType} '{principalValue}' is ambiguous. Matches: {string.Join(", ", candidates.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}.",
            nameof(principalValue))
    {
        PrincipalType = principalType;
        PrincipalValue = principalValue;
        Candidates = candidates.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public PowerPlatformPrincipalType PrincipalType { get; }

    public string PrincipalValue { get; }

    public IReadOnlyList<string> Candidates { get; }
}
#pragma warning restore RS0030

/// <summary>
/// Resolves human-facing tenant role inputs (principal identifiers and role
/// values) into the concrete strategy/action required to manage assignments.
/// Future <c>txc tenant</c> commands should depend on this resolver instead of
/// talking to the PP-RBAC or legacy BAP clients directly.
/// </summary>
public sealed class TenantRoleResolver
{
    private readonly MicrosoftGraphClient _graph;
    private readonly PowerPlatformRbacRoleStrategy _rbacStrategy;
    private readonly BapAdminApplicationRoleStrategy _bapStrategy;

    public TenantRoleResolver(
        MicrosoftGraphClient graph,
        PowerPlatformRbacRoleStrategy rbacStrategy,
        BapAdminApplicationRoleStrategy bapStrategy)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _rbacStrategy = rbacStrategy ?? throw new ArgumentNullException(nameof(rbacStrategy));
        _bapStrategy = bapStrategy ?? throw new ArgumentNullException(nameof(bapStrategy));
    }

    public Task<IReadOnlyList<PowerPlatformRoleDefinition>> ListTenantRolesAsync(
        Connection connection,
        Credential credential,
        string? filter,
        CancellationToken ct)
        => ListTenantRolesCoreAsync(connection, credential, filter, ct);

    public async Task<PowerPlatformRoleDefinition> GetTenantRoleAsync(
        Connection connection,
        Credential credential,
        string roleNameOrId,
        CancellationToken ct)
        => await _rbacStrategy.ResolveTenantRoleAsync(connection, credential, roleNameOrId, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListAssignmentsAsync(
        Connection connection,
        Credential credential,
        PowerPlatformPrincipalType principalType,
        string principalValue,
        CancellationToken ct)
    {
        var principal = await ResolvePrincipalAsync(connection, credential, principalType, principalValue, ct)
            .ConfigureAwait(false);

        var assignments = new List<PowerPlatformTenantRoleAssignment>();
        assignments.AddRange(await _rbacStrategy.ListAsync(connection, credential, principal, ct).ConfigureAwait(false));

        if (principalType == PowerPlatformPrincipalType.ApplicationUser)
        {
            assignments.AddRange(await _bapStrategy.ListAsync(connection, credential, principal, ct).ConfigureAwait(false));
        }

        return assignments
            .OrderBy(a => a.RoleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task AddAssignmentAsync(
        Connection connection,
        Credential credential,
        PowerPlatformPrincipalType principalType,
        string principalValue,
        string roleNameOrId,
        CancellationToken ct)
    {
        if (string.Equals(roleNameOrId, BapAdminApplicationRoleStrategy.AdminApplicationRoleValue, StringComparison.OrdinalIgnoreCase)
            && principalType != PowerPlatformPrincipalType.ApplicationUser)
        {
            throw new ArgumentException(
                $"The synthetic role '{BapAdminApplicationRoleStrategy.AdminApplicationRoleValue}' is only valid for application principals.",
                nameof(roleNameOrId));
        }

        var principal = await ResolvePrincipalAsync(connection, credential, principalType, principalValue, ct)
            .ConfigureAwait(false);

        if (string.Equals(roleNameOrId, BapAdminApplicationRoleStrategy.AdminApplicationRoleValue, StringComparison.OrdinalIgnoreCase))
        {
            await _bapStrategy.AddAsync(connection, credential, principal, roleNameOrId, ct).ConfigureAwait(false);
            return;
        }

        var role = await _rbacStrategy.ResolveTenantRoleAsync(connection, credential, roleNameOrId, ct).ConfigureAwait(false);
        await _rbacStrategy.AddAsync(connection, credential, principal, role, ct).ConfigureAwait(false);
    }

    public async Task RemoveAssignmentAsync(
        Connection connection,
        Credential credential,
        PowerPlatformPrincipalType principalType,
        string principalValue,
        string roleNameOrId,
        CancellationToken ct)
    {
        if (string.Equals(roleNameOrId, BapAdminApplicationRoleStrategy.AdminApplicationRoleValue, StringComparison.OrdinalIgnoreCase)
            && principalType != PowerPlatformPrincipalType.ApplicationUser)
        {
            throw new ArgumentException(
                $"The synthetic role '{BapAdminApplicationRoleStrategy.AdminApplicationRoleValue}' is only valid for application principals.",
                nameof(roleNameOrId));
        }

        var principal = await ResolvePrincipalAsync(connection, credential, principalType, principalValue, ct)
            .ConfigureAwait(false);

        if (string.Equals(roleNameOrId, BapAdminApplicationRoleStrategy.AdminApplicationRoleValue, StringComparison.OrdinalIgnoreCase))
        {
            await _bapStrategy.RemoveAsync(connection, credential, principal, roleNameOrId, ct).ConfigureAwait(false);
            return;
        }

        var role = await _rbacStrategy.ResolveTenantRoleAsync(connection, credential, roleNameOrId, ct).ConfigureAwait(false);
        await _rbacStrategy.RemoveAsync(connection, credential, principal, role, ct).ConfigureAwait(false);
    }

    internal async Task<PowerPlatformRolePrincipalReference> ResolvePrincipalAsync(
        Connection connection,
        Credential credential,
        PowerPlatformPrincipalType principalType,
        string principalValue,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalValue);

        return principalType switch
        {
            PowerPlatformPrincipalType.ApplicationUser => await ResolveApplicationAsync(connection, credential, principalValue, ct).ConfigureAwait(false),
            PowerPlatformPrincipalType.User => await ResolveUserAsync(connection, credential, principalValue, ct).ConfigureAwait(false),
            PowerPlatformPrincipalType.Group => await ResolveGroupAsync(connection, credential, principalValue, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(principalType), principalType, "Unsupported tenant principal type."),
        };
    }

    private async Task<IReadOnlyList<PowerPlatformRoleDefinition>> ListTenantRolesCoreAsync(
        Connection connection,
        Credential credential,
        string? filter,
        CancellationToken ct)
    {
        var roles = await _rbacStrategy.ListTenantAssignableRoleDefinitionsAsync(connection, credential, ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(filter))
            return roles;

        return roles
            .Where(role => role.RoleDefinitionName.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)
                || role.RoleDefinitionId.ToString().Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<PowerPlatformRolePrincipalReference> ResolveApplicationAsync(
        Connection connection,
        Credential credential,
        string principalValue,
        CancellationToken ct)
    {
        var filter = BuildServicePrincipalFilter(principalValue);
        var matches = await _graph.ListServicePrincipalsAsync(connection, credential, filter, top: 25, ct)
            .ConfigureAwait(false);

        var normalized = principalValue.Trim();
        var exactMatches = matches.Where(sp => MatchesApplication(sp, normalized)).ToList();
        return ResolveSingle(
            PowerPlatformPrincipalType.ApplicationUser,
            principalValue,
            exactMatches,
            sp => sp.DisplayName ?? sp.AppId?.ToString() ?? sp.Id.ToString(),
            sp => new PowerPlatformRolePrincipalReference(
                PowerPlatformPrincipalType.ApplicationUser,
                sp.Id,
                sp.AppId,
                sp.DisplayName));
    }

    private async Task<PowerPlatformRolePrincipalReference> ResolveUserAsync(
        Connection connection,
        Credential credential,
        string principalValue,
        CancellationToken ct)
    {
        var filter = BuildUserFilter(principalValue);
        var matches = await _graph.ListUsersAsync(connection, credential, filter, top: 25, ct)
            .ConfigureAwait(false);

        var normalized = principalValue.Trim();
        var exactMatches = matches.Where(user => MatchesUser(user, normalized)).ToList();
        return ResolveSingle(
            PowerPlatformPrincipalType.User,
            principalValue,
            exactMatches,
            user => user.UserPrincipalName ?? user.DisplayName ?? user.Id.ToString(),
            user => new PowerPlatformRolePrincipalReference(
                PowerPlatformPrincipalType.User,
                user.Id,
                DisplayName: user.DisplayName,
                UserPrincipalName: user.UserPrincipalName));
    }

    private async Task<PowerPlatformRolePrincipalReference> ResolveGroupAsync(
        Connection connection,
        Credential credential,
        string principalValue,
        CancellationToken ct)
    {
        var filter = BuildGroupFilter(principalValue);
        var matches = await _graph.ListGroupsAsync(connection, credential, filter, top: 25, ct)
            .ConfigureAwait(false);

        var normalized = principalValue.Trim();
        var exactMatches = matches.Where(group => MatchesGroup(group, normalized)).ToList();
        return ResolveSingle(
            PowerPlatformPrincipalType.Group,
            principalValue,
            exactMatches,
            group => group.DisplayName ?? group.Id.ToString(),
            group => new PowerPlatformRolePrincipalReference(
                PowerPlatformPrincipalType.Group,
                group.Id,
                DisplayName: group.DisplayName));
    }

    // Microsoft Graph rejects an entire $filter expression with a 400 if any clause compares a
    // Guid-typed property (id, appId) to a value that isn't a valid GUID literal - even when that
    // clause is combined with "or" against a valid string clause. So the id/appId eq clauses must
    // only be included when the supplied value actually parses as a GUID.
    private static string BuildServicePrincipalFilter(string value)
    {
        var trimmed = value.Trim();
        var escaped = EscapeODataString(trimmed);
        var displayNameClause = $"displayName eq '{escaped}'";

        if (!Guid.TryParse(trimmed, out _))
            return displayNameClause;

        return $"appId eq '{escaped}' or id eq '{escaped}' or {displayNameClause}";
    }

    private static string BuildUserFilter(string value)
    {
        var trimmed = value.Trim();
        var escaped = EscapeODataString(trimmed);

        if (!Guid.TryParse(trimmed, out _))
            return $"userPrincipalName eq '{escaped}'";

        return $"id eq '{escaped}' or userPrincipalName eq '{escaped}'";
    }

    private static string BuildGroupFilter(string value)
    {
        var trimmed = value.Trim();
        var escaped = EscapeODataString(trimmed);

        if (!Guid.TryParse(trimmed, out _))
            return $"displayName eq '{escaped}'";

        return $"id eq '{escaped}' or displayName eq '{escaped}'";
    }

    private static bool MatchesApplication(GraphServicePrincipal principal, string input)
        => principal.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || (principal.AppId?.ToString().Equals(input, StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(principal.DisplayName, input, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesUser(GraphUser user, string input)
        => user.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || string.Equals(user.UserPrincipalName, input, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesGroup(GraphGroup group, string input)
        => group.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || string.Equals(group.DisplayName, input, StringComparison.OrdinalIgnoreCase);

    private static PowerPlatformRolePrincipalReference ResolveSingle<TSource>(
        PowerPlatformPrincipalType principalType,
        string principalValue,
        IReadOnlyList<TSource> matches,
        Func<TSource, string> candidateText,
        Func<TSource, PowerPlatformRolePrincipalReference> projector)
    {
        if (matches.Count == 0)
            throw new TenantPrincipalNotFoundException(principalType, principalValue);

        if (matches.Count > 1)
            throw new TenantPrincipalAmbiguousException(principalType, principalValue, matches.Select(candidateText));

        return projector(matches[0]);
    }

    private static string EscapeODataString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
