using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Features.Tenant.User;

internal static class UserCommandSupport
{
    public static async Task<IReadOnlyList<GraphUser>> ListUsersAsync(
        string? profile,
        string? filter,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
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
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
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

    public static async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListRolesAsync(
        string? profile,
        string user,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        return await resolver.ListAssignmentsAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.User,
            user,
            ct).ConfigureAwait(false);
    }

    public static async Task AddRoleAsync(
        string? profile,
        string user,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.AddAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.User,
            user,
            role,
            ct).ConfigureAwait(false);
    }

    public static async Task RemoveRoleAsync(
        string? profile,
        string user,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.RemoveAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.User,
            user,
            role,
            ct).ConfigureAwait(false);
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
