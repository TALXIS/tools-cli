using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

internal static class DataverseSecurityPrincipalManager
{
    private const string BusinessUnitAlias = "businessunit";
    private const string SystemUserRoleRelationshipName = "systemuserroles_association";
    private const string TeamRoleRelationshipName = "teamroles_association";
    private const string TeamMembershipRelationshipName = "teammembership_association";

    private static readonly ColumnSet SystemUserColumns = new(
        "systemuserid",
        "fullname",
        "domainname",
        "internalemailaddress",
        "azureactivedirectoryobjectid",
        "isdisabled",
        "applicationid",
        "businessunitid");

    private static readonly ColumnSet TeamColumns = new(
        "teamid",
        "name",
        "teamtype",
        "azureactivedirectoryobjectid",
        "membershiptype",
        "businessunitid",
        "isdefault",
        "systemmanaged");

    private static readonly ColumnSet RoleColumns = new(
        "roleid",
        "name",
        "businessunitid");

    public static async Task<IReadOnlyList<DataverseUserRecord>> ListRegularUsersAsync(
        IOrganizationServiceAsync2 service,
        DataverseSecurityPrincipalStateFilter filter,
        CancellationToken ct)
    {
        var query = CreateSystemUserQuery(includeServicePrincipals: false);
        ApplyEnabledStateFilter(query.Criteria, filter);
        query.AddOrder("fullname", OrderType.Ascending);
        query.AddOrder("domainname", OrderType.Ascending);

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Select(ToRegularUserRecord).ToList();
    }

    public static async Task<DataverseUserRecord?> GetRegularUserAsync(
        IOrganizationServiceAsync2 service,
        string userIdOrUpn,
        CancellationToken ct)
    {
        var matches = await QueryRegularUsersAsync(service, userIdOrUpn, ct).ConfigureAwait(false);
        return ResolveOptionalSingle(matches, "Dataverse user", userIdOrUpn, static user => new DataverseLookupCandidate(
            user.Id,
            user.FullName ?? user.UserPrincipalName ?? "(unnamed user)",
            user.UserPrincipalName));
    }

    public static async Task UpdateRegularUserEnabledStateAsync(
        IOrganizationServiceAsync2 service,
        string userIdOrUpn,
        bool enabled,
        CancellationToken ct)
    {
        var user = await RequireRegularUserAsync(service, userIdOrUpn, ct).ConfigureAwait(false);
        var entity = new Entity("systemuser", user.Id)
        {
            ["isdisabled"] = !enabled,
        };

        await service.UpdateAsync(entity, ct).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<DataverseRoleRecord>> ListRegularUserRolesAsync(
        IOrganizationServiceAsync2 service,
        string userIdOrUpn,
        CancellationToken ct)
    {
        var user = await RequireRegularUserAsync(service, userIdOrUpn, ct).ConfigureAwait(false);
        return await ListRelatedRolesAsync(service, "systemuser", user.Id, SystemUserRoleRelationshipName, ct).ConfigureAwait(false);
    }

    public static async Task AddRegularUserRoleAsync(
        IOrganizationServiceAsync2 service,
        string userIdOrUpn,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        var user = await RequireRegularUserAsync(service, userIdOrUpn, ct).ConfigureAwait(false);
        var role = await RequireRoleAsync(service, roleNameOrGuid, ct).ConfigureAwait(false);
        Associate("systemuser", user.Id, "role", role.Id, SystemUserRoleRelationshipName, service);
    }

    public static async Task RemoveRegularUserRoleAsync(
        IOrganizationServiceAsync2 service,
        string userIdOrUpn,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        var user = await RequireRegularUserAsync(service, userIdOrUpn, ct).ConfigureAwait(false);
        var role = await RequireRoleAsync(service, roleNameOrGuid, ct).ConfigureAwait(false);
        Disassociate("systemuser", user.Id, "role", role.Id, SystemUserRoleRelationshipName, service);
    }

    public static async Task<IReadOnlyList<DataverseServicePrincipalRecord>> ListServicePrincipalsAsync(
        IOrganizationServiceAsync2 service,
        DataverseSecurityPrincipalStateFilter filter,
        CancellationToken ct)
    {
        var query = CreateSystemUserQuery(includeServicePrincipals: true);
        ApplyEnabledStateFilter(query.Criteria, filter);
        query.AddOrder("fullname", OrderType.Ascending);
        query.AddOrder("applicationid", OrderType.Ascending);

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Select(ToServicePrincipalRecord).ToList();
    }

    public static async Task<DataverseServicePrincipalRecord?> GetServicePrincipalAsync(
        IOrganizationServiceAsync2 service,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        var matches = await QueryServicePrincipalsAsync(service, clientIdOrGuid, ct).ConfigureAwait(false);
        return ResolveOptionalSingle(matches, "Dataverse service principal", clientIdOrGuid, static app => new DataverseLookupCandidate(
            app.Id,
            app.FullName ?? app.ApplicationId.ToString(),
            app.ApplicationId.ToString()));
    }

    public static async Task<DataverseServicePrincipalRecord> CreateServicePrincipalAsync(
        IOrganizationServiceAsync2 service,
        DataverseServicePrincipalCreateOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var businessUnitId = await ResolveBusinessUnitIdAsync(service, options.BusinessUnitIdOrName, ct).ConfigureAwait(false);
        var entity = new Entity("systemuser")
        {
            ["applicationid"] = options.EntraClientId,
            ["businessunitid"] = new EntityReference("businessunit", businessUnitId),
        };

        var id = await service.CreateAsync(entity, ct).ConfigureAwait(false);

        foreach (var roleIdentifier in DistinctIdentifiers(options.InitialRoleNamesOrGuids))
        {
            var role = await RequireRoleAsync(service, roleIdentifier, ct).ConfigureAwait(false);
            Associate("systemuser", id, "role", role.Id, SystemUserRoleRelationshipName, service);
        }

        return await GetServicePrincipalBySystemUserIdAsync(service, id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Service principal '{id}' was created but could not be reloaded.");
    }

    public static async Task UpdateServicePrincipalEnabledStateAsync(
        IOrganizationServiceAsync2 service,
        string clientIdOrGuid,
        bool enabled,
        CancellationToken ct)
    {
        var app = await RequireServicePrincipalAsync(service, clientIdOrGuid, ct).ConfigureAwait(false);
        var entity = new Entity("systemuser", app.Id)
        {
            ["isdisabled"] = !enabled,
        };

        await service.UpdateAsync(entity, ct).ConfigureAwait(false);
    }

    public static async Task DeleteServicePrincipalAsync(
        IOrganizationServiceAsync2 service,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        var app = await RequireServicePrincipalAsync(service, clientIdOrGuid, ct).ConfigureAwait(false);
        if (!app.IsDisabled)
            throw new InvalidOperationException($"Service principal '{clientIdOrGuid}' must be disabled before it can be deleted.");

        await service.DeleteAsync("systemuser", app.Id, ct).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<DataverseRoleRecord>> ListServicePrincipalRolesAsync(
        IOrganizationServiceAsync2 service,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        var app = await RequireServicePrincipalAsync(service, clientIdOrGuid, ct).ConfigureAwait(false);
        return await ListRelatedRolesAsync(service, "systemuser", app.Id, SystemUserRoleRelationshipName, ct).ConfigureAwait(false);
    }

    public static async Task AddServicePrincipalRoleAsync(
        IOrganizationServiceAsync2 service,
        string clientIdOrGuid,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        var app = await RequireServicePrincipalAsync(service, clientIdOrGuid, ct).ConfigureAwait(false);
        var role = await RequireRoleAsync(service, roleNameOrGuid, ct).ConfigureAwait(false);
        Associate("systemuser", app.Id, "role", role.Id, SystemUserRoleRelationshipName, service);
    }

    public static async Task RemoveServicePrincipalRoleAsync(
        IOrganizationServiceAsync2 service,
        string clientIdOrGuid,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        var app = await RequireServicePrincipalAsync(service, clientIdOrGuid, ct).ConfigureAwait(false);
        var role = await RequireRoleAsync(service, roleNameOrGuid, ct).ConfigureAwait(false);
        Disassociate("systemuser", app.Id, "role", role.Id, SystemUserRoleRelationshipName, service);
    }

    public static async Task<IReadOnlyList<DataverseTeamRecord>> ListTeamsAsync(
        IOrganizationServiceAsync2 service,
        CancellationToken ct)
    {
        var query = CreateTeamQuery();
        query.AddOrder("name", OrderType.Ascending);

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Select(ToTeamRecord).ToList();
    }

    public static async Task<DataverseTeamRecord?> GetTeamAsync(
        IOrganizationServiceAsync2 service,
        string nameOrGuid,
        CancellationToken ct)
    {
        var matches = await QueryTeamsAsync(service, nameOrGuid, ct).ConfigureAwait(false);
        return ResolveOptionalSingle(matches, "Dataverse team", nameOrGuid, static team => new DataverseLookupCandidate(
            team.Id,
            team.Name,
            FormatTeamType(team.TeamType)));
    }

    public static async Task<DataverseTeamRecord> CreateTeamAsync(
        IOrganizationServiceAsync2 service,
        DataverseTeamCreateOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Name))
            throw new ArgumentException("Team name is required.", nameof(options));

        if (IsAadManagedTeamType(options.TeamType) && options.AadObjectId is null)
            throw new ArgumentException("AAD-backed team types require an Azure AD object ID.", nameof(options));

        var businessUnitId = await ResolveBusinessUnitIdAsync(service, options.BusinessUnitIdOrName, ct).ConfigureAwait(false);
        var entity = new Entity("team")
        {
            ["name"] = options.Name,
            ["teamtype"] = new OptionSetValue(ToTeamTypeValue(options.TeamType)),
            ["businessunitid"] = new EntityReference("businessunit", businessUnitId),
        };

        if (options.AadObjectId.HasValue)
            entity["azureactivedirectoryobjectid"] = options.AadObjectId.Value;

        if (options.MembershipType.HasValue)
            entity["membershiptype"] = new OptionSetValue(ToMembershipTypeValue(options.MembershipType.Value));

        var id = await service.CreateAsync(entity, ct).ConfigureAwait(false);
        return await GetTeamByIdAsync(service, id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Team '{id}' was created but could not be reloaded.");
    }

    public static async Task DeleteTeamAsync(
        IOrganizationServiceAsync2 service,
        string nameOrGuid,
        CancellationToken ct)
    {
        var team = await RequireTeamAsync(service, nameOrGuid, ct).ConfigureAwait(false);
        await service.DeleteAsync("team", team.Id, ct).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<DataverseUserRecord>> ListTeamMembersAsync(
        IOrganizationServiceAsync2 service,
        string teamIdOrName,
        CancellationToken ct)
    {
        var team = await RequireTeamAsync(service, teamIdOrName, ct).ConfigureAwait(false);
        var relationship = new Relationship(TeamMembershipRelationshipName);
        var related = await RetrieveRelatedEntitiesAsync(
            service,
            "team",
            team.Id,
            relationship,
            CreateSystemUserQuery(includeServicePrincipals: false),
            ct).ConfigureAwait(false);

        return related
            .Select(ToRegularUserRecord)
            .OrderBy(static member => member.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static member => member.UserPrincipalName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task AddTeamMemberAsync(
        IOrganizationServiceAsync2 service,
        string teamIdOrName,
        string userIdOrUpn,
        CancellationToken ct)
    {
        var team = await RequireTeamAsync(service, teamIdOrName, ct).ConfigureAwait(false);
        EnsureTeamMembershipCanBeManaged(team, "Adding");
        var user = await RequireRegularUserAsync(service, userIdOrUpn, ct).ConfigureAwait(false);
        Associate("team", team.Id, "systemuser", user.Id, TeamMembershipRelationshipName, service);
    }

    public static async Task RemoveTeamMemberAsync(
        IOrganizationServiceAsync2 service,
        string teamIdOrName,
        string userIdOrUpn,
        CancellationToken ct)
    {
        var team = await RequireTeamAsync(service, teamIdOrName, ct).ConfigureAwait(false);
        EnsureTeamMembershipCanBeManaged(team, "Removing");
        var user = await RequireRegularUserAsync(service, userIdOrUpn, ct).ConfigureAwait(false);
        Disassociate("team", team.Id, "systemuser", user.Id, TeamMembershipRelationshipName, service);
    }

    public static async Task<IReadOnlyList<DataverseRoleRecord>> ListTeamRolesAsync(
        IOrganizationServiceAsync2 service,
        string teamIdOrName,
        CancellationToken ct)
    {
        var team = await RequireTeamAsync(service, teamIdOrName, ct).ConfigureAwait(false);
        return await ListRelatedRolesAsync(service, "team", team.Id, TeamRoleRelationshipName, ct).ConfigureAwait(false);
    }

    public static async Task AddTeamRoleAsync(
        IOrganizationServiceAsync2 service,
        string teamIdOrName,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        var team = await RequireTeamAsync(service, teamIdOrName, ct).ConfigureAwait(false);
        var role = await RequireRoleAsync(service, roleNameOrGuid, ct).ConfigureAwait(false);
        Associate("team", team.Id, "role", role.Id, TeamRoleRelationshipName, service);
    }

    public static async Task RemoveTeamRoleAsync(
        IOrganizationServiceAsync2 service,
        string teamIdOrName,
        string roleNameOrGuid,
        CancellationToken ct)
    {
        var team = await RequireTeamAsync(service, teamIdOrName, ct).ConfigureAwait(false);
        var role = await RequireRoleAsync(service, roleNameOrGuid, ct).ConfigureAwait(false);
        Disassociate("team", team.Id, "role", role.Id, TeamRoleRelationshipName, service);
    }

    public static async Task<IReadOnlyList<DataverseRoleRecord>> ListRolesAsync(
        IOrganizationServiceAsync2 service,
        string? filter,
        CancellationToken ct)
    {
        var query = CreateRoleQuery();
        if (!string.IsNullOrWhiteSpace(filter))
            query.Criteria.AddCondition("name", ConditionOperator.Like, $"%{filter.Trim()}%");

        query.AddOrder("name", OrderType.Ascending);

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Select(ToRoleRecord).ToList();
    }

    public static async Task<DataverseRoleRecord?> GetRoleAsync(
        IOrganizationServiceAsync2 service,
        string nameOrGuid,
        CancellationToken ct)
    {
        var matches = await QueryRolesAsync(service, nameOrGuid, ct).ConfigureAwait(false);
        return ResolveOptionalSingle(matches, "Dataverse role", nameOrGuid, static role => new DataverseLookupCandidate(
            role.Id,
            role.Name,
            role.BusinessUnitName));
    }

    internal static T? ResolveOptionalSingle<T>(
        IReadOnlyList<T> matches,
        string entityDisplayName,
        string identifier,
        Func<T, DataverseLookupCandidate> candidateFactory)
        where T : class
    {
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new DataverseAmbiguousMatchException(
                entityDisplayName,
                identifier,
                matches.Select(candidateFactory).ToList()),
        };
    }

    internal static void EnsureTeamMembershipCanBeManaged(DataverseTeamRecord team, string action)
    {
        if (!IsAadManagedTeamType(team.TeamType))
            return;

        throw new InvalidOperationException(
            $"{action} members is not supported for team '{team.Name}' because {FormatTeamType(team.TeamType)} membership is managed in Entra ID.");
    }

    internal static bool IsAadManagedTeamType(DataverseTeamType teamType)
        => teamType is DataverseTeamType.AadSecurityGroup or DataverseTeamType.AadOfficeGroup;

    internal static int ToTeamTypeValue(DataverseTeamType teamType) => teamType switch
    {
        DataverseTeamType.Owner => 0,
        DataverseTeamType.Access => 1,
        DataverseTeamType.AadSecurityGroup => 2,
        DataverseTeamType.AadOfficeGroup => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(teamType), teamType, "Unsupported Dataverse team type."),
    };

    internal static DataverseTeamType FromTeamTypeValue(int teamTypeValue) => teamTypeValue switch
    {
        0 => DataverseTeamType.Owner,
        1 => DataverseTeamType.Access,
        2 => DataverseTeamType.AadSecurityGroup,
        3 => DataverseTeamType.AadOfficeGroup,
        _ => throw new ArgumentOutOfRangeException(nameof(teamTypeValue), teamTypeValue, "Unsupported Dataverse team type value."),
    };

    internal static int ToMembershipTypeValue(DataverseTeamMembershipType membershipType) => membershipType switch
    {
        DataverseTeamMembershipType.MembersAndGuests => 0,
        DataverseTeamMembershipType.Members => 1,
        DataverseTeamMembershipType.Owners => 2,
        DataverseTeamMembershipType.Guests => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(membershipType), membershipType, "Unsupported Dataverse membership type."),
    };

    internal static DataverseTeamMembershipType FromMembershipTypeValue(int membershipTypeValue) => membershipTypeValue switch
    {
        0 => DataverseTeamMembershipType.MembersAndGuests,
        1 => DataverseTeamMembershipType.Members,
        2 => DataverseTeamMembershipType.Owners,
        3 => DataverseTeamMembershipType.Guests,
        _ => throw new ArgumentOutOfRangeException(nameof(membershipTypeValue), membershipTypeValue, "Unsupported Dataverse membership type value."),
    };

    internal static string FormatTeamType(DataverseTeamType teamType) => teamType switch
    {
        DataverseTeamType.Owner => "owner-team",
        DataverseTeamType.Access => "access-team",
        DataverseTeamType.AadSecurityGroup => "aad-security-group team",
        DataverseTeamType.AadOfficeGroup => "aad-office-group team",
        _ => teamType.ToString(),
    };

    private static async Task<DataverseUserRecord> RequireRegularUserAsync(
        IOrganizationServiceAsync2 service,
        string userIdOrUpn,
        CancellationToken ct)
        => await GetRegularUserAsync(service, userIdOrUpn, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Dataverse user '{userIdOrUpn}' was not found.");

    private static async Task<DataverseServicePrincipalRecord> RequireServicePrincipalAsync(
        IOrganizationServiceAsync2 service,
        string clientIdOrGuid,
        CancellationToken ct)
        => await GetServicePrincipalAsync(service, clientIdOrGuid, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Dataverse service principal '{clientIdOrGuid}' was not found.");

    private static async Task<DataverseTeamRecord> RequireTeamAsync(
        IOrganizationServiceAsync2 service,
        string nameOrGuid,
        CancellationToken ct)
        => await GetTeamAsync(service, nameOrGuid, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Dataverse team '{nameOrGuid}' was not found.");

    private static async Task<DataverseRoleRecord> RequireRoleAsync(
        IOrganizationServiceAsync2 service,
        string nameOrGuid,
        CancellationToken ct)
        => await GetRoleAsync(service, nameOrGuid, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Dataverse role '{nameOrGuid}' was not found.");

    private static async Task<IReadOnlyList<DataverseUserRecord>> QueryRegularUsersAsync(
        IOrganizationServiceAsync2 service,
        string userIdOrUpn,
        CancellationToken ct)
    {
        var query = CreateSystemUserQuery(includeServicePrincipals: false);
        ApplyRegularUserIdentifierFilter(query.Criteria, userIdOrUpn);

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Select(ToRegularUserRecord).ToList();
    }

    private static async Task<IReadOnlyList<DataverseServicePrincipalRecord>> QueryServicePrincipalsAsync(
        IOrganizationServiceAsync2 service,
        string clientIdOrGuid,
        CancellationToken ct)
    {
        var query = CreateSystemUserQuery(includeServicePrincipals: true);
        ApplyServicePrincipalIdentifierFilter(query.Criteria, clientIdOrGuid);

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Select(ToServicePrincipalRecord).ToList();
    }

    private static async Task<DataverseServicePrincipalRecord?> GetServicePrincipalBySystemUserIdAsync(
        IOrganizationServiceAsync2 service,
        Guid systemUserId,
        CancellationToken ct)
    {
        var query = CreateSystemUserQuery(includeServicePrincipals: true);
        query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, systemUserId);

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Count == 0 ? null : ToServicePrincipalRecord(entities[0]);
    }

    private static async Task<IReadOnlyList<DataverseTeamRecord>> QueryTeamsAsync(
        IOrganizationServiceAsync2 service,
        string nameOrGuid,
        CancellationToken ct)
    {
        var query = CreateTeamQuery();
        if (Guid.TryParse(nameOrGuid, out var id))
        {
            query.Criteria.AddCondition("teamid", ConditionOperator.Equal, id);
        }
        else
        {
            query.Criteria.AddCondition("name", ConditionOperator.Equal, nameOrGuid);
        }

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Select(ToTeamRecord).ToList();
    }

    private static async Task<DataverseTeamRecord?> GetTeamByIdAsync(
        IOrganizationServiceAsync2 service,
        Guid teamId,
        CancellationToken ct)
    {
        var query = CreateTeamQuery();
        query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Count == 0 ? null : ToTeamRecord(entities[0]);
    }

    private static async Task<IReadOnlyList<DataverseRoleRecord>> QueryRolesAsync(
        IOrganizationServiceAsync2 service,
        string nameOrGuid,
        CancellationToken ct)
    {
        var query = CreateRoleQuery();
        if (Guid.TryParse(nameOrGuid, out var id))
        {
            query.Criteria.AddCondition("roleid", ConditionOperator.Equal, id);
        }
        else
        {
            query.Criteria.AddCondition("name", ConditionOperator.Equal, nameOrGuid);
        }

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        return entities.Select(ToRoleRecord).ToList();
    }

    private static QueryExpression CreateSystemUserQuery(bool includeServicePrincipals)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = SystemUserColumns,
        };

        query.Criteria.AddCondition(
            "applicationid",
            includeServicePrincipals ? ConditionOperator.NotNull : ConditionOperator.Null);

        AddBusinessUnitLink(query, "businessunitid", "businessunitid");
        return query;
    }

    private static QueryExpression CreateTeamQuery()
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = TeamColumns,
        };

        AddBusinessUnitLink(query, "businessunitid", "businessunitid");
        return query;
    }

    private static QueryExpression CreateRoleQuery()
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = RoleColumns,
        };

        AddBusinessUnitLink(query, "businessunitid", "businessunitid");
        return query;
    }

    private static void AddBusinessUnitLink(QueryExpression query, string fromAttributeName, string toAttributeName)
    {
        var link = query.AddLink("businessunit", fromAttributeName, toAttributeName, JoinOperator.LeftOuter);
        link.EntityAlias = BusinessUnitAlias;
        link.Columns = new ColumnSet("name");
    }

    private static void ApplyEnabledStateFilter(FilterExpression criteria, DataverseSecurityPrincipalStateFilter filter)
    {
        switch (filter)
        {
            case DataverseSecurityPrincipalStateFilter.Enabled:
                criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
                break;
            case DataverseSecurityPrincipalStateFilter.Disabled:
                criteria.AddCondition("isdisabled", ConditionOperator.Equal, true);
                break;
            case DataverseSecurityPrincipalStateFilter.All:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unsupported Dataverse enabled-state filter.");
        }
    }

    private static void ApplyRegularUserIdentifierFilter(FilterExpression criteria, string userIdOrUpn)
    {
        if (Guid.TryParse(userIdOrUpn, out var id))
        {
            criteria.AddCondition("systemuserid", ConditionOperator.Equal, id);
            return;
        }

        var identifierFilter = new FilterExpression(LogicalOperator.Or);
        identifierFilter.AddCondition("domainname", ConditionOperator.Equal, userIdOrUpn);
        identifierFilter.AddCondition("internalemailaddress", ConditionOperator.Equal, userIdOrUpn);
        criteria.AddFilter(identifierFilter);
    }

    private static void ApplyServicePrincipalIdentifierFilter(FilterExpression criteria, string clientIdOrGuid)
    {
        if (!Guid.TryParse(clientIdOrGuid, out var id))
            throw new ArgumentException("Service principals can only be resolved by system user GUID or application client ID GUID.", nameof(clientIdOrGuid));

        var identifierFilter = new FilterExpression(LogicalOperator.Or);
        identifierFilter.AddCondition("systemuserid", ConditionOperator.Equal, id);
        identifierFilter.AddCondition("applicationid", ConditionOperator.Equal, id);
        criteria.AddFilter(identifierFilter);
    }

    private static DataverseUserRecord ToRegularUserRecord(Entity entity) => new(
        Id: entity.Id,
        FullName: entity.GetAttributeValue<string>("fullname"),
        UserPrincipalName: entity.GetAttributeValue<string>("domainname"),
        PrimaryEmailAddress: entity.GetAttributeValue<string>("internalemailaddress"),
        AzureActiveDirectoryObjectId: GetNullableGuid(entity, "azureactivedirectoryobjectid"),
        IsDisabled: entity.GetAttributeValue<bool>("isdisabled"),
        BusinessUnitId: GetEntityReferenceId(entity, "businessunitid"),
        BusinessUnitName: GetAliasedString(entity, BusinessUnitAlias, "name"));

    private static DataverseServicePrincipalRecord ToServicePrincipalRecord(Entity entity) => new(
        Id: entity.Id,
        ApplicationId: entity.GetAttributeValue<Guid>("applicationid"),
        FullName: entity.GetAttributeValue<string>("fullname"),
        AzureActiveDirectoryObjectId: GetNullableGuid(entity, "azureactivedirectoryobjectid"),
        IsDisabled: entity.GetAttributeValue<bool>("isdisabled"),
        BusinessUnitId: GetEntityReferenceId(entity, "businessunitid"),
        BusinessUnitName: GetAliasedString(entity, BusinessUnitAlias, "name"));

    private static DataverseTeamRecord ToTeamRecord(Entity entity) => new(
        Id: entity.Id,
        Name: entity.GetAttributeValue<string>("name") ?? "(unnamed team)",
        TeamType: FromTeamTypeValue(entity.GetAttributeValue<OptionSetValue>("teamtype")?.Value ?? entity.GetAttributeValue<int>("teamtype")),
        AzureActiveDirectoryObjectId: GetNullableGuid(entity, "azureactivedirectoryobjectid"),
        MembershipType: TryGetMembershipType(entity),
        BusinessUnitId: GetEntityReferenceId(entity, "businessunitid"),
        BusinessUnitName: GetAliasedString(entity, BusinessUnitAlias, "name"),
        IsDefault: entity.GetAttributeValue<bool>("isdefault"),
        IsSystemManaged: entity.GetAttributeValue<bool>("systemmanaged"));

    private static DataverseRoleRecord ToRoleRecord(Entity entity) => new(
        Id: entity.Id,
        Name: entity.GetAttributeValue<string>("name") ?? "(unnamed role)",
        BusinessUnitId: GetEntityReferenceId(entity, "businessunitid"),
        BusinessUnitName: GetAliasedString(entity, BusinessUnitAlias, "name"));

    private static DataverseTeamMembershipType? TryGetMembershipType(Entity entity)
    {
        if (!entity.Attributes.TryGetValue("membershiptype", out var value) || value is null)
            return null;

        var optionValue = value switch
        {
            OptionSetValue optionSet => optionSet.Value,
            int intValue => intValue,
            _ => throw new InvalidOperationException($"Unexpected membershiptype payload: {value.GetType().FullName}.")
        };

        return FromMembershipTypeValue(optionValue);
    }

    private static Guid? GetNullableGuid(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
            return null;

        return value switch
        {
            Guid guidValue => guidValue,
            _ => throw new InvalidOperationException($"Unexpected GUID payload for '{attributeName}': {value.GetType().FullName}.")
        };
    }

    private static Guid? GetEntityReferenceId(Entity entity, string attributeName)
    {
        var reference = entity.GetAttributeValue<EntityReference>(attributeName);
        return reference?.Id;
    }

    private static string? GetAliasedString(Entity entity, string alias, string attributeName)
    {
        var key = $"{alias}.{attributeName}";
        if (!entity.Attributes.TryGetValue(key, out var value) || value is not AliasedValue aliased || aliased.Value is not string text)
            return null;

        return text;
    }

    private static async Task<Guid> ResolveBusinessUnitIdAsync(
        IOrganizationServiceAsync2 service,
        string? businessUnitIdOrName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(businessUnitIdOrName))
            return await GetCurrentBusinessUnitIdAsync(service, ct).ConfigureAwait(false);

        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet("businessunitid", "name"),
        };

        if (Guid.TryParse(businessUnitIdOrName, out var id))
        {
            query.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, id);
        }
        else
        {
            query.Criteria.AddCondition("name", ConditionOperator.Equal, businessUnitIdOrName);
        }

        var entities = await RetrieveAllAsync(service, query, ct).ConfigureAwait(false);
        var matches = entities
            .Select(static entity => new DataverseLookupCandidate(
                entity.Id,
                entity.GetAttributeValue<string>("name") ?? "(unnamed business unit)",
                null))
            .ToList();

        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"Business unit '{businessUnitIdOrName}' was not found."),
            1 => matches[0].Id,
            _ => throw new DataverseAmbiguousMatchException("Dataverse business unit", businessUnitIdOrName, matches),
        };
    }

    private static async Task<Guid> GetCurrentBusinessUnitIdAsync(
        IOrganizationServiceAsync2 service,
        CancellationToken ct)
    {
        var whoAmI = (WhoAmIResponse)await service.ExecuteAsync(new WhoAmIRequest(), ct).ConfigureAwait(false);
        var user = await service.RetrieveAsync("systemuser", whoAmI.UserId, new ColumnSet("businessunitid"), ct).ConfigureAwait(false);
        return user.GetAttributeValue<EntityReference>("businessunitid")?.Id
            ?? throw new InvalidOperationException("The current Dataverse caller does not have an associated business unit.");
    }

    private static async Task<IReadOnlyList<DataverseRoleRecord>> ListRelatedRolesAsync(
        IOrganizationServiceAsync2 service,
        string parentEntityName,
        Guid parentId,
        string relationshipName,
        CancellationToken ct)
    {
        var query = CreateRoleQuery();
        query.AddOrder("name", OrderType.Ascending);

        var related = await RetrieveRelatedEntitiesAsync(
            service,
            parentEntityName,
            parentId,
            new Relationship(relationshipName),
            query,
            ct).ConfigureAwait(false);

        return related.Select(ToRoleRecord).ToList();
    }

    private static async Task<List<Entity>> RetrieveRelatedEntitiesAsync(
        IOrganizationServiceAsync2 service,
        string parentEntityName,
        Guid parentId,
        Relationship relationship,
        QueryExpression relatedQuery,
        CancellationToken ct)
    {
        var results = new List<Entity>();
        var pageNumber = 1;
        string? pagingCookie = null;

        while (true)
        {
            relatedQuery.PageInfo = new PagingInfo
            {
                Count = 5000,
                PageNumber = pageNumber,
                PagingCookie = pagingCookie,
            };

            var request = new RetrieveRequest
            {
                Target = new EntityReference(parentEntityName, parentId),
                ColumnSet = new ColumnSet(false),
                RelatedEntitiesQuery = new RelationshipQueryCollection
                {
                    { relationship, relatedQuery }
                }
            };

            var response = (RetrieveResponse)await service.ExecuteAsync(request, ct).ConfigureAwait(false);
            var collection = ExtractRelatedCollection(response, relationship);
            results.AddRange(collection.Entities);

            if (!collection.MoreRecords)
                return results;

            pageNumber++;
            pagingCookie = collection.PagingCookie;
        }
    }

    private static EntityCollection ExtractRelatedCollection(RetrieveResponse response, Relationship relationship)
    {
        if (response.Entity.RelatedEntities.TryGetValue(relationship, out var collection))
            return collection;

        foreach (var pair in response.Entity.RelatedEntities)
        {
            if (string.Equals(pair.Key.SchemaName, relationship.SchemaName, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return new EntityCollection();
    }

    private static async Task<List<Entity>> RetrieveAllAsync(
        IOrganizationServiceAsync2 service,
        QueryExpression query,
        CancellationToken ct)
    {
        var results = new List<Entity>();
        var pageNumber = 1;
        string? pagingCookie = null;

        while (true)
        {
            query.PageInfo = new PagingInfo
            {
                Count = 5000,
                PageNumber = pageNumber,
                PagingCookie = pagingCookie,
            };

            var response = await service.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            results.AddRange(response.Entities);

            if (!response.MoreRecords)
                return results;

            pageNumber++;
            pagingCookie = response.PagingCookie;
        }
    }

    private static IEnumerable<string> DistinctIdentifiers(IReadOnlyList<string> identifiers)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var identifier in identifiers)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                continue;

            var trimmed = identifier.Trim();
            if (seen.Add(trimmed))
                yield return trimmed;
        }
    }

    private static void Associate(
        string entityName,
        Guid recordId,
        string targetEntityName,
        Guid targetRecordId,
        string relationshipName,
        IOrganizationServiceAsync2 service)
    {
        service.Associate(
            entityName,
            recordId,
            new Relationship(relationshipName),
            [new EntityReference(targetEntityName, targetRecordId)]);
    }

    private static void Disassociate(
        string entityName,
        Guid recordId,
        string targetEntityName,
        Guid targetRecordId,
        string relationshipName,
        IOrganizationServiceAsync2 service)
    {
        service.Disassociate(
            entityName,
            recordId,
            new Relationship(relationshipName),
            [new EntityReference(targetEntityName, targetRecordId)]);
    }
}
