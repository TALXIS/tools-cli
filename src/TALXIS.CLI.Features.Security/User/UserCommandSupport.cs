using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Features.Security.User;

internal static class UserCommandSupport
{
    public static async Task<IReadOnlyList<GraphUser>> ListUsersAsync(
        string? profile,
        string? filter,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var graph = TxcServices.Get<MicrosoftGraphClient>();
        return await graph.ListUsersAsync(
            context.Connection,
            context.Credential,
            BuildListFilter(filter),
            top: 100,
            ct).ConfigureAwait(false);
    }

    public static async Task<GraphUser> GetUserAsync(
        string? profile,
        string user,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var graph = TxcServices.Get<MicrosoftGraphClient>();
        var matches = await graph.ListUsersAsync(
            context.Connection,
            context.Credential,
            BuildGetFilter(user),
            top: 25,
            ct).ConfigureAwait(false);

        var exactMatches = matches
            .Where(candidate => MatchesUser(candidate, user.Trim()))
            .ToList();

        if (exactMatches.Count == 0)
            throw new TenantPrincipalNotFoundException(PowerPlatformPrincipalType.User, user);

        if (exactMatches.Count > 1)
        {
            throw new TenantPrincipalAmbiguousException(
                PowerPlatformPrincipalType.User,
                user,
                exactMatches.Select(FormatCandidate));
        }

        return exactMatches[0];
    }

    public static async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListTenantRolesAsync(
        string? profile,
        string user,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<SecurityRoleResolver>();
        return await resolver.ListAssignmentsAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.User,
            user,
            ct).ConfigureAwait(false);
    }

    public static async Task AddTenantRoleAsync(
        string? profile,
        string user,
        string role,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<SecurityRoleResolver>();
        await resolver.AddAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.User,
            user,
            role,
            ct).ConfigureAwait(false);
    }

    public static async Task RemoveTenantRoleAsync(
        string? profile,
        string user,
        string role,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<SecurityRoleResolver>();
        await resolver.RemoveAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.User,
            user,
            role,
            ct).ConfigureAwait(false);
    }

    public static async Task<DataverseUserRecord?> ResolveEnvironmentUserAsync(
        IDataverseUserService service,
        string? profileName,
        string userIdOrUpn,
        Guid? environmentId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var user = await service.GetAsync(profileName, userIdOrUpn, ct, environmentId).ConfigureAwait(false);
            if (user is null)
                logger.LogError("Dataverse user '{User}' was not found.", userIdOrUpn);

            return user;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            LogDataverseAmbiguousMatch(logger, ex);
            return null;
        }
    }

    public static async Task<DataverseRoleRecord?> ResolveEnvironmentRoleAsync(
        IDataverseRoleService service,
        string? profileName,
        string roleNameOrGuid,
        Guid? environmentId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var role = await service.GetAsync(profileName, roleNameOrGuid, ct, environmentId).ConfigureAwait(false);
            if (role is null)
                logger.LogError("Dataverse role '{Role}' was not found.", roleNameOrGuid);

            return role;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            LogDataverseAmbiguousMatch(logger, ex);
            return null;
        }
    }

    public static void LogDataverseAmbiguousMatch(ILogger logger, DataverseAmbiguousMatchException ex)
    {
        logger.LogError("Multiple {EntityDisplayName} records matched '{Identifier}'.", ex.EntityDisplayName, ex.Identifier);
        foreach (var candidate in ex.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Description))
            {
                logger.LogError("  - {Name} ({Id})", candidate.Name, candidate.Id);
            }
            else
            {
                logger.LogError("  - {Name} [{Description}] ({Id})", candidate.Name, candidate.Description, candidate.Id);
            }
        }
    }

    public static string FormatUserLabel(DataverseUserRecord user)
        => user.UserPrincipalName
            ?? user.PrimaryEmailAddress
            ?? user.FullName
            ?? user.Id.ToString();

    public static void PrintEnvironmentUsersTable(IReadOnlyList<DataverseUserRecord> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No environment users found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => (r.FullName ?? string.Empty).Length), 4, 28);
        int upnWidth = Math.Clamp(rows.Max(r => (r.UserPrincipalName ?? string.Empty).Length), 3, 36);
        int emailWidth = Math.Clamp(rows.Max(r => (r.PrimaryEmailAddress ?? string.Empty).Length), 5, 36);
        int stateWidth = 8;
        int buWidth = Math.Clamp(rows.Max(r => (r.BusinessUnitName ?? string.Empty).Length), 13, 28);

        string header =
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"UPN".PadRight(upnWidth)} | " +
            $"{"Email".PadRight(emailWidth)} | " +
            $"{"State".PadRight(stateWidth)} | " +
            $"{"Business Unit".PadRight(buWidth)} | User ID";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{SecurityPrincipalCommandSupport.Truncate(row.FullName ?? string.Empty, nameWidth).PadRight(nameWidth)} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.UserPrincipalName ?? string.Empty, upnWidth).PadRight(upnWidth)} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.PrimaryEmailAddress ?? string.Empty, emailWidth).PadRight(emailWidth)} | " +
                $"{(row.IsDisabled ? "disabled" : "enabled").PadRight(stateWidth)} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? string.Empty, buWidth).PadRight(buWidth)} | {row.Id}");
        }
#pragma warning restore TXC003
    }

    public static void PrintEnvironmentUserDetail(DataverseUserRecord user)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine($"User ID:         {user.Id}");
        OutputWriter.WriteLine($"Name:            {user.FullName ?? "-"}");
        OutputWriter.WriteLine($"UPN:             {user.UserPrincipalName ?? "-"}");
        OutputWriter.WriteLine($"Email:           {user.PrimaryEmailAddress ?? "-"}");
        OutputWriter.WriteLine($"Entra Object ID: {user.AzureActiveDirectoryObjectId?.ToString() ?? "-"}");
        OutputWriter.WriteLine($"State:           {(user.IsDisabled ? "disabled" : "enabled")}");
        OutputWriter.WriteLine($"Business Unit:   {user.BusinessUnitName ?? "-"}");
#pragma warning restore TXC003
    }

    public static void PrintEnvironmentRolesTable(IReadOnlyList<DataverseRoleRecord> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No security roles assigned.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.Name.Length), 4, 48);
        int buWidth = Math.Clamp(rows.Max(r => (r.BusinessUnitName ?? string.Empty).Length), 13, 28);
        string header = $"{"Role".PadRight(nameWidth)} | {"Business Unit".PadRight(buWidth)} | Role ID";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            OutputWriter.WriteLine(
                $"{SecurityPrincipalCommandSupport.Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? string.Empty, buWidth).PadRight(buWidth)} | {row.Id}");
        }
#pragma warning restore TXC003
    }

    internal static void PrintUserList(IReadOnlyList<GraphUser> users)
    {
#pragma warning disable TXC003
        if (users.Count == 0)
        {
            OutputWriter.WriteLine("No users found.");
            return;
        }

        int upnWidth = Math.Clamp(users.Max(u => (u.UserPrincipalName ?? string.Empty).Length), 3, 48);
        int nameWidth = Math.Clamp(users.Max(u => (u.DisplayName ?? string.Empty).Length), 12, 36);

        string header =
            $"{"UPN".PadRight(upnWidth)} | " +
            $"{"Display Name".PadRight(nameWidth)} | " +
            "Object ID";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var user in users)
        {
            OutputWriter.WriteLine(
                $"{Truncate(user.UserPrincipalName ?? string.Empty, upnWidth).PadRight(upnWidth)} | " +
                $"{Truncate(user.DisplayName ?? string.Empty, nameWidth).PadRight(nameWidth)} | " +
                $"{user.Id}");
        }
#pragma warning restore TXC003
    }

    internal static void PrintUserDetail(GraphUser user)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine($"UPN:          {user.UserPrincipalName ?? "-"}");
        OutputWriter.WriteLine($"Display Name: {user.DisplayName ?? "-"}");
        OutputWriter.WriteLine($"Object ID:    {user.Id}");
#pragma warning restore TXC003
    }

    private static string? BuildListFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var escaped = GraphODataFilterSupport.EscapeODataString(filter.Trim());
        return $"startswith(userPrincipalName,'{escaped}') or startswith(displayName,'{escaped}')";
    }

    private static string BuildGetFilter(string user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        return GraphODataFilterSupport.BuildIdentifierFilter(user, ["id"], ["userPrincipalName"]);
    }

    private static bool MatchesUser(GraphUser user, string input)
        => user.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || string.Equals(user.UserPrincipalName, input, StringComparison.OrdinalIgnoreCase);

    private static string FormatCandidate(GraphUser user)
        => string.IsNullOrWhiteSpace(user.UserPrincipalName)
            ? $"{user.DisplayName ?? "(no display name)"} ({user.Id})"
            : $"{user.UserPrincipalName} ({user.Id})";

    private static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
