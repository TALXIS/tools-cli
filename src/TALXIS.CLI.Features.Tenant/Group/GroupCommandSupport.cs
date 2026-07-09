using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Tenant;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Features.Tenant.Group;

internal static class GroupCommandSupport
{
    public static async Task<IReadOnlyList<GraphGroup>> ListGroupsAsync(
        string? profile,
        string? filter,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var graph = TxcServices.Get<MicrosoftGraphClient>();
        return await graph.ListGroupsAsync(
            context.Connection,
            context.Credential,
            BuildListFilter(filter),
            top: 100,
            ct).ConfigureAwait(false);
    }

    public static async Task<GraphGroup> GetGroupAsync(
        string? profile,
        string group,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var graph = TxcServices.Get<MicrosoftGraphClient>();
        var matches = await graph.ListGroupsAsync(
            context.Connection,
            context.Credential,
            BuildGetFilter(group),
            top: 25,
            ct).ConfigureAwait(false);

        var exactMatches = matches
            .Where(candidate => MatchesGroup(candidate, group.Trim()))
            .ToList();

        if (exactMatches.Count == 0)
            throw new TenantPrincipalNotFoundException(PowerPlatformPrincipalType.Group, group);

        if (exactMatches.Count > 1)
        {
            throw new TenantPrincipalAmbiguousException(
                PowerPlatformPrincipalType.Group,
                group,
                exactMatches.Select(FormatCandidate));
        }

        return exactMatches[0];
    }

    public static async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListRolesAsync(
        string? profile,
        string group,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        return await resolver.ListAssignmentsAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.Group,
            group,
            ct).ConfigureAwait(false);
    }

    public static async Task AddRoleAsync(
        string? profile,
        string group,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.AddAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.Group,
            group,
            role,
            ct).ConfigureAwait(false);
    }

    public static async Task RemoveRoleAsync(
        string? profile,
        string group,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.RemoveAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.Group,
            group,
            role,
            ct).ConfigureAwait(false);
    }

    internal static void PrintGroupList(IReadOnlyList<GraphGroup> groups)
    {
#pragma warning disable TXC003
        if (groups.Count == 0)
        {
            OutputWriter.WriteLine("No groups found.");
            return;
        }

        int nameWidth = Math.Clamp(groups.Max(g => (g.DisplayName ?? string.Empty).Length), 12, 48);

        string header =
            $"{"Display Name".PadRight(nameWidth)} | " +
            "Object ID";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var group in groups)
        {
            OutputWriter.WriteLine(
                $"{Truncate(group.DisplayName ?? string.Empty, nameWidth).PadRight(nameWidth)} | " +
                $"{group.Id}");
        }
#pragma warning restore TXC003
    }

    internal static void PrintGroupDetail(GraphGroup group)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine($"Display Name: {group.DisplayName ?? "-"}");
        OutputWriter.WriteLine($"Object ID:    {group.Id}");
#pragma warning restore TXC003
    }

    private static string? BuildListFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var escaped = TenantPrincipalCommandSupport.EscapeODataString(filter.Trim());
        return $"startswith(displayName,'{escaped}')";
    }

    private static string BuildGetFilter(string group)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        var escaped = TenantPrincipalCommandSupport.EscapeODataString(group.Trim());
        return $"id eq '{escaped}' or displayName eq '{escaped}'";
    }

    private static bool MatchesGroup(GraphGroup group, string input)
        => group.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || string.Equals(group.DisplayName, input, StringComparison.OrdinalIgnoreCase);

    private static string FormatCandidate(GraphGroup group)
        => $"{group.DisplayName ?? "(no display name)"} ({group.Id})";

    private static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
