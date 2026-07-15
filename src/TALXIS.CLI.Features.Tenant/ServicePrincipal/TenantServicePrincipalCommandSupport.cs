using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Features.Tenant.ServicePrincipal;

internal static class TenantServicePrincipalCommandSupport
{
    public static async Task<IReadOnlyList<GraphServicePrincipal>> ListServicePrincipalsAsync(
        string? profile,
        string? filter,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var graph = TxcServices.Get<MicrosoftGraphClient>();
        return await graph.ListServicePrincipalsAsync(
            context.Connection,
            context.Credential,
            BuildListFilter(filter),
            top: 100,
            ct).ConfigureAwait(false);
    }

    public static async Task<GraphServicePrincipal> GetServicePrincipalAsync(
        string? profile,
        string app,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var graph = TxcServices.Get<MicrosoftGraphClient>();
        var matches = await graph.ListServicePrincipalsAsync(
            context.Connection,
            context.Credential,
            BuildExactAppFilter(app),
            top: 25,
            ct).ConfigureAwait(false);

        var normalized = app.Trim();
        var exactMatches = matches.Where(candidate => MatchesApplication(candidate, normalized)).ToList();

        if (exactMatches.Count == 0)
            throw new TenantPrincipalNotFoundException(PowerPlatformPrincipalType.ApplicationUser, app);

        if (exactMatches.Count > 1)
        {
            throw new TenantPrincipalAmbiguousException(
                PowerPlatformPrincipalType.ApplicationUser,
                app,
                exactMatches.Select(FormatAppCandidate));
        }

        return exactMatches[0];
    }

    public static async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListAssignmentsAsync(
        string? profile,
        string app,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        return await resolver.ListAssignmentsAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.ApplicationUser,
            app,
            ct).ConfigureAwait(false);
    }

    public static async Task AddAssignmentAsync(
        string? profile,
        string app,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.AddAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.ApplicationUser,
            app,
            role,
            ct).ConfigureAwait(false);
    }

    public static async Task RemoveAssignmentAsync(
        string? profile,
        string app,
        string role,
        CancellationToken ct)
    {
        var context = await TenantPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.RemoveAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.ApplicationUser,
            app,
            role,
            ct).ConfigureAwait(false);
    }

    internal static void WriteServicePrincipalTable(IReadOnlyList<GraphServicePrincipal> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No tenant service principals found.");
            return;
        }

        const int objectIdWidth = 36;
        const int appIdWidth = 36;
        int displayNameWidth = Math.Clamp(rows.Max(r => (r.DisplayName ?? string.Empty).Length), 12, 48);

        string header =
            $"{"Application ID".PadRight(appIdWidth)} | " +
            $"{"Object ID".PadRight(objectIdWidth)} | " +
            "Display Name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length + displayNameWidth));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{(row.AppId?.ToString() ?? "-").PadRight(appIdWidth)} | " +
                $"{row.Id} | " +
                $"{TenantPrincipalCommandSupport.Truncate(row.DisplayName ?? string.Empty, displayNameWidth)}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteServicePrincipalDetail(GraphServicePrincipal app)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine($"Application ID: {(app.AppId?.ToString() ?? "-")}");
        OutputWriter.WriteLine($"Object ID:      {app.Id}");
        OutputWriter.WriteLine($"Display Name:   {app.DisplayName ?? "-"}");
#pragma warning restore TXC003
    }

    internal static void WriteRoleTable(IReadOnlyList<PowerPlatformTenantRoleAssignment> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No tenant service-principal role assignments found.");
            return;
        }

        int roleWidth = Math.Clamp(rows.Max(r => r.RoleName.Length), 4, 36);
        int identifierWidth = Math.Clamp(rows.Max(r => r.RoleIdentifier.Length), 10, 36);
        int kindWidth = 11;

        string header =
            $"{"Role".PadRight(roleWidth)} | " +
            $"{"Identifier".PadRight(identifierWidth)} | " +
            $"{"Kind".PadRight(kindWidth)} | " +
            "Scope";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length + 24));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{TenantPrincipalCommandSupport.Truncate(row.RoleName, roleWidth).PadRight(roleWidth)} | " +
                $"{TenantPrincipalCommandSupport.Truncate(row.RoleIdentifier, identifierWidth).PadRight(identifierWidth)} | " +
                $"{(row.IsSynthetic ? "Synthetic" : "Tenant role").PadRight(kindWidth)} | " +
                $"{row.Scope}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteMutationResult<T>(T payload, Action textRenderer)
        => OutputFormatter.WriteData(payload, _ => textRenderer());

    private static string? BuildListFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        return $"startswith(displayName,'{GraphODataFilterSupport.EscapeODataString(filter.Trim())}')";
    }

    private static string BuildExactAppFilter(string app)
        => GraphODataFilterSupport.BuildIdentifierFilter(app, ["appId", "id"], ["displayName"]);

    private static bool MatchesApplication(GraphServicePrincipal principal, string input)
        => principal.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || (principal.AppId?.ToString().Equals(input, StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(principal.DisplayName, input, StringComparison.OrdinalIgnoreCase);

    private static string FormatAppCandidate(GraphServicePrincipal principal)
        => $"{principal.DisplayName ?? "-"} (appId: {principal.AppId?.ToString() ?? "-"}, id: {principal.Id})";
}
