using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Features.Tenant.App;

internal static class TenantAppCommandSupport
{
    public static async Task<IReadOnlyList<GraphServicePrincipal>> ListAppsAsync(
        string? profile,
        string? filter,
        CancellationToken ct)
    {
        var context = await ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var graph = TxcServices.Get<MicrosoftGraphClient>();
        return await graph.ListServicePrincipalsAsync(
            context.Connection,
            context.Credential,
            BuildListFilter(filter),
            top: 100,
            ct).ConfigureAwait(false);
    }

    public static async Task<GraphServicePrincipal> GetAppAsync(
        string? profile,
        string app,
        CancellationToken ct)
    {
        var context = await ResolveContextAsync(profile, ct).ConfigureAwait(false);
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
        var context = await ResolveContextAsync(profile, ct).ConfigureAwait(false);
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
        var context = await ResolveContextAsync(profile, ct).ConfigureAwait(false);
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
        var context = await ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<TenantRoleResolver>();
        await resolver.RemoveAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.ApplicationUser,
            app,
            role,
            ct).ConfigureAwait(false);
    }

    internal static bool TryHandleValidationException(ILogger logger, Exception ex, out int exitCode)
    {
        if (ex is TenantPrincipalAmbiguousException ambiguousPrincipal)
        {
            logger.LogError("{Error}", ambiguousPrincipal.Message);
            foreach (var candidate in ambiguousPrincipal.Candidates)
                logger.LogError("Candidate: {Candidate}", candidate);

            exitCode = 2;
            return true;
        }

        if (ex is TenantRoleAmbiguousException ambiguousRole)
        {
            logger.LogError("{Error}", ambiguousRole.Message);
            foreach (var candidate in ambiguousRole.CandidateNames)
                logger.LogError("Candidate: {Candidate}", candidate);

            exitCode = 2;
            return true;
        }

        if (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogError("{Error}", ex.Message);
            exitCode = 2;
            return true;
        }

        exitCode = 0;
        return false;
    }

    internal static void WriteAppTable(IReadOnlyList<GraphServicePrincipal> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No tenant apps found.");
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
                $"{Truncate(row.DisplayName ?? string.Empty, displayNameWidth)}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteAppDetail(GraphServicePrincipal app)
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
            OutputWriter.WriteLine("No tenant app role assignments found.");
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
                $"{Truncate(row.RoleName, roleWidth).PadRight(roleWidth)} | " +
                $"{Truncate(row.RoleIdentifier, identifierWidth).PadRight(identifierWidth)} | " +
                $"{(row.IsSynthetic ? "Synthetic" : "Tenant role").PadRight(kindWidth)} | " +
                $"{row.Scope}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteMutationResult<T>(T payload, Action textRenderer)
        => OutputFormatter.WriteData(payload, _ => textRenderer());

    private static Task<ResolvedProfileContext> ResolveContextAsync(string? profile, CancellationToken ct)
    {
        var configurationResolver = TxcServices.Get<IConfigurationResolver>();
        return configurationResolver.ResolveAsync(profile, ct);
    }

    private static string? BuildListFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        return $"startswith(displayName,'{EscapeODataString(filter.Trim())}')";
    }

    // Microsoft Graph rejects an entire $filter expression with a 400 if any clause compares a
    // Guid-typed property (id, appId) to a value that isn't a valid GUID literal - even when
    // combined with "or" against a valid string clause. So the appId/id eq clauses must only be
    // included when the supplied value actually parses as a GUID (mirrors
    // TenantRoleResolver.BuildServicePrincipalFilter).
    private static string BuildExactAppFilter(string app)
    {
        var trimmed = app.Trim();
        var escaped = EscapeODataString(trimmed);
        var displayNameClause = $"displayName eq '{escaped}'";

        if (!Guid.TryParse(trimmed, out _))
            return displayNameClause;

        return $"appId eq '{escaped}' or id eq '{escaped}' or {displayNameClause}";
    }

    private static bool MatchesApplication(GraphServicePrincipal principal, string input)
        => principal.Id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase)
            || (principal.AppId?.ToString().Equals(input, StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(principal.DisplayName, input, StringComparison.OrdinalIgnoreCase);

    private static string FormatAppCandidate(GraphServicePrincipal principal)
        => $"{principal.DisplayName ?? "-"} (appId: {principal.AppId?.ToString() ?? "-"}, id: {principal.Id})";

    private static string EscapeODataString(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
