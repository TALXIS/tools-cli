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
    private readonly IReadOnlyList<IPowerPlatformRoleAssignmentStrategy> _strategies;

    public TenantRoleResolver(
        MicrosoftGraphClient graph,
        PowerPlatformRbacRoleStrategy rbacStrategy,
        BapAdminApplicationRoleStrategy bapStrategy)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _rbacStrategy = rbacStrategy ?? throw new ArgumentNullException(nameof(rbacStrategy));
        ArgumentNullException.ThrowIfNull(bapStrategy);

        // Ordered list of strategies this resolver can dispatch to. Adding a
        // new tenant-role strategy (e.g. another synthetic role) only requires
        // implementing IPowerPlatformRoleAssignmentStrategy and adding it here -
        // no other resolver logic needs to change (Open/Closed).
        _strategies = [_rbacStrategy, bapStrategy];
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
        foreach (var strategy in _strategies.Where(s => s.SupportsPrincipalType(principalType)))
        {
            assignments.AddRange(await strategy.ListAsync(connection, credential, principal, ct).ConfigureAwait(false));
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
        var strategy = ResolveStrategy(principalType, roleNameOrId);

        var principal = await ResolvePrincipalAsync(connection, credential, principalType, principalValue, ct)
            .ConfigureAwait(false);

        await strategy.AddAsync(connection, credential, principal, roleNameOrId, ct).ConfigureAwait(false);
    }

    public async Task RemoveAssignmentAsync(
        Connection connection,
        Credential credential,
        PowerPlatformPrincipalType principalType,
        string principalValue,
        string roleNameOrId,
        CancellationToken ct)
    {
        var strategy = ResolveStrategy(principalType, roleNameOrId);

        var principal = await ResolvePrincipalAsync(connection, credential, principalType, principalValue, ct)
            .ConfigureAwait(false);

        await strategy.RemoveAsync(connection, credential, principal, roleNameOrId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the single strategy that owns Add/Remove dispatch for the given
    /// principal type + role identifier, without hardcoding per-strategy
    /// checks here (see <see cref="IPowerPlatformRoleAssignmentStrategy.CanHandle"/>).
    /// </summary>
    private IPowerPlatformRoleAssignmentStrategy ResolveStrategy(PowerPlatformPrincipalType principalType, string roleNameOrId)
    {
        var strategy = _strategies.FirstOrDefault(s => s.CanHandle(principalType, roleNameOrId));
        if (strategy is null)
        {
            throw new ArgumentException(
                $"The synthetic role '{BapAdminApplicationRoleStrategy.AdminApplicationRoleValue}' is only valid for application principals.",
                nameof(roleNameOrId));
        }

        return strategy;
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
            PowerPlatformPrincipalType.Group => ResolveGroup(principalValue),
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

    // Unlike users and applications, groups are never resolved through Microsoft Graph.
    // Searching/resolving a group by display name requires the Graph "Group.Read.All"
    // permission, which is not pre-consented for this CLI's Entra app registration in
    // most tenants - and we deliberately never prompt tenant admins for extra consent.
    // Instead, the caller must supply the group's Entra object id (GUID) directly, the
    // same approach "pac admin assign-group" uses (its --group argument is a raw GUID).
    private static PowerPlatformRolePrincipalReference ResolveGroup(string principalValue)
    {
        var trimmed = principalValue.Trim();
        if (!Guid.TryParse(trimmed, out var objectId))
        {
            throw new ArgumentException(
                $"Group '{principalValue}' must be specified as an Entra object id (GUID). " +
                "This CLI does not look up groups by display name to avoid requiring the " +
                "Microsoft Graph 'Group.Read.All' permission. Find the object id via the Entra " +
                "admin center or 'az ad group show --group <name> --query id -o tsv'.",
                nameof(principalValue));
        }

        return new PowerPlatformRolePrincipalReference(PowerPlatformPrincipalType.Group, objectId);
    }

    private static string BuildServicePrincipalFilter(string value)
        => GraphODataFilterSupport.BuildIdentifierFilter(value, ["appId", "id"], ["displayName"]);

    private static string BuildUserFilter(string value)
        => GraphODataFilterSupport.BuildIdentifierFilter(value, ["id"], ["userPrincipalName"]);

    private static bool MatchesApplication(GraphServicePrincipal principal, string input)
        => principal.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || (principal.AppId?.ToString().Equals(input, StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(principal.DisplayName, input, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesUser(GraphUser user, string input)
        => user.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || string.Equals(user.UserPrincipalName, input, StringComparison.OrdinalIgnoreCase);

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
}
