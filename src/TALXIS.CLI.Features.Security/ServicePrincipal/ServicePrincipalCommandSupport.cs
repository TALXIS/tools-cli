using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

internal static class ServicePrincipalCommandSupport
{
    public static async Task<IReadOnlyList<GraphServicePrincipal>> ListServicePrincipalsAsync(
        string? profile,
        string? filter,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
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
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
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

    public static async Task<IReadOnlyList<PowerPlatformTenantRoleAssignment>> ListTenantAssignmentsAsync(
        string? profile,
        string app,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<SecurityRoleResolver>();
        return await resolver.ListAssignmentsAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.ApplicationUser,
            app,
            ct).ConfigureAwait(false);
    }

    public static async Task AddTenantAssignmentAsync(
        string? profile,
        string app,
        string role,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<SecurityRoleResolver>();
        await resolver.AddAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.ApplicationUser,
            app,
            role,
            ct).ConfigureAwait(false);
    }

    public static async Task RemoveTenantAssignmentAsync(
        string? profile,
        string app,
        string role,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<SecurityRoleResolver>();
        await resolver.RemoveAssignmentAsync(
            context.Connection,
            context.Credential,
            PowerPlatformPrincipalType.ApplicationUser,
            app,
            role,
            ct).ConfigureAwait(false);
    }

    internal static bool TryResolveEnabledState(
        bool enable,
        bool disable,
        ILogger logger,
        out bool enabled)
    {
        if (enable == disable)
        {
            logger.LogError("Specify exactly one of --enable or --disable.");
            enabled = false;
            return false;
        }

        enabled = enable;
        return true;
    }

    internal static void WriteServicePrincipalDetails(DataverseServicePrincipalRecord app)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine($"System User ID: {app.Id}");
        OutputWriter.WriteLine($"Application ID: {app.ApplicationId}");
        OutputWriter.WriteLine($"Name:           {app.FullName ?? "-"}");
        OutputWriter.WriteLine($"State:          {(app.IsDisabled ? "Disabled" : "Enabled")}");
        OutputWriter.WriteLine($"Business Unit:  {app.BusinessUnitName ?? "-"}");
        OutputWriter.WriteLine($"Business Unit ID: {(app.BusinessUnitId?.ToString() ?? "-")}");
        OutputWriter.WriteLine($"Entra Object ID: {(app.AzureActiveDirectoryObjectId?.ToString() ?? "-")}");
#pragma warning restore TXC003
    }

    internal static void WriteEnvironmentServicePrincipalTable(IReadOnlyList<DataverseServicePrincipalRecord> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No service principals found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(static r => (r.FullName ?? string.Empty).Length), 4, 36);
        int stateWidth = 8;
        int businessUnitWidth = Math.Clamp(rows.Max(static r => (r.BusinessUnitName ?? string.Empty).Length), 13, 36);

        string header =
            $"{"System User ID".PadRight(36)} | " +
            $"{"Application ID".PadRight(36)} | " +
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"State".PadRight(stateWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{row.Id} | " +
                $"{row.ApplicationId} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.FullName ?? string.Empty, nameWidth).PadRight(nameWidth)} | " +
                $"{(row.IsDisabled ? "Disabled" : "Enabled").PadRight(stateWidth)} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? string.Empty, businessUnitWidth).PadRight(businessUnitWidth)}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteEnvironmentRoleTable(IReadOnlyList<DataverseRoleRecord> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No security roles assigned.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(static r => r.Name.Length), 4, 48);
        int businessUnitWidth = Math.Clamp(rows.Max(static r => (r.BusinessUnitName ?? string.Empty).Length), 13, 36);

        string header =
            $"{"Role ID".PadRight(36)} | " +
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{row.Id} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? string.Empty, businessUnitWidth).PadRight(businessUnitWidth)}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteCreateResult(
        DataverseServicePrincipalRecord app,
        IReadOnlyList<string> assignedRoles,
        IReadOnlyList<ServicePrincipalRoleAssignmentFailure> failures)
    {
        var payload = new
        {
            status = failures.Count == 0 ? "created" : "partial",
            servicePrincipal = app,
            assignedRoles,
            failedRoles = failures.Select(static failure => new
            {
                role = failure.Role,
                error = failure.Message,
            }).ToArray(),
        };

        OutputFormatter.WriteData(payload, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine(failures.Count == 0
                ? "Service principal created."
                : "Service principal created, but one or more role assignments failed.");
            WriteServicePrincipalDetails(app);

            if (assignedRoles.Count > 0)
            {
                OutputWriter.WriteLine();
                OutputWriter.WriteLine($"Assigned roles ({assignedRoles.Count}):");
                foreach (var role in assignedRoles)
                    OutputWriter.WriteLine($"  - {role}");
            }

            if (failures.Count > 0)
            {
                OutputWriter.WriteLine();
                OutputWriter.WriteLine($"Role assignment failures ({failures.Count}):");
                foreach (var failure in failures)
                    OutputWriter.WriteLine($"  - {failure.Role}: {failure.Message}");
            }
#pragma warning restore TXC003
        });
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
                $"{SecurityPrincipalCommandSupport.Truncate(row.DisplayName ?? string.Empty, displayNameWidth)}");
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

internal sealed record ServicePrincipalRoleAssignmentFailure(string Role, string Message, bool IsValidationError);
