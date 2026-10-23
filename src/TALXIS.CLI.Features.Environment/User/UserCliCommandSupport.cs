using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.User;

internal static class UserCliCommandSupport
{
    public static bool TryResolveStateFilter(
        bool enabled,
        bool disabled,
        bool all,
        ILogger logger,
        out DataverseSecurityPrincipalStateFilter filter)
        => EnvironmentPrincipalCommandSupport.TryResolveStateFilter(enabled, disabled, all, logger, out filter);

    public static async Task<DataverseUserRecord?> ResolveUserAsync(
        IDataverseUserService service,
        string? profileName,
        string userIdOrUpn,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var user = await service.GetAsync(profileName, userIdOrUpn, ct).ConfigureAwait(false);
            if (user is null)
                logger.LogError("Dataverse user '{User}' was not found.", userIdOrUpn);

            return user;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            LogAmbiguousMatch(logger, ex);
            return null;
        }
    }

    public static async Task<DataverseRoleRecord?> ResolveRoleAsync(
        IDataverseRoleService service,
        string? profileName,
        string roleNameOrGuid,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var role = await service.GetAsync(profileName, roleNameOrGuid, ct).ConfigureAwait(false);
            if (role is null)
                logger.LogError("Dataverse role '{Role}' was not found.", roleNameOrGuid);

            return role;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            LogAmbiguousMatch(logger, ex);
            return null;
        }
    }

    public static void LogAmbiguousMatch(ILogger logger, DataverseAmbiguousMatchException ex)
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

    public static void PrintUsersTable(IReadOnlyList<DataverseUserRecord> rows)
    {
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
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.FullName ?? string.Empty, nameWidth).PadRight(nameWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.UserPrincipalName ?? string.Empty, upnWidth).PadRight(upnWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.PrimaryEmailAddress ?? string.Empty, emailWidth).PadRight(emailWidth)} | " +
                $"{(row.IsDisabled ? "disabled" : "enabled").PadRight(stateWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? string.Empty, buWidth).PadRight(buWidth)} | {row.Id}");
        }
    }

    public static void PrintUserDetail(DataverseUserRecord user)
    {
        OutputWriter.WriteLine($"User ID:         {user.Id}");
        OutputWriter.WriteLine($"Name:            {user.FullName ?? "-"}");
        OutputWriter.WriteLine($"UPN:             {user.UserPrincipalName ?? "-"}");
        OutputWriter.WriteLine($"Email:           {user.PrimaryEmailAddress ?? "-"}");
        OutputWriter.WriteLine($"Entra Object ID: {user.AzureActiveDirectoryObjectId?.ToString() ?? "-"}");
        OutputWriter.WriteLine($"State:           {(user.IsDisabled ? "disabled" : "enabled")}");
        OutputWriter.WriteLine($"Business Unit:   {user.BusinessUnitName ?? "-"}");
    }

    public static void PrintRolesTable(IReadOnlyList<DataverseRoleRecord> rows)
    {
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
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? string.Empty, buWidth).PadRight(buWidth)} | {row.Id}");
        }
    }

    public static bool TryParseRoleIdentifiers(
        string? csv,
        ILogger logger,
        out IReadOnlyList<string> roles)
        => EnvironmentPrincipalCommandSupport.TryParseRoleIdentifiers(csv, logger, out roles);

    public static bool TryHandleValidationException(ILogger logger, Exception ex, out int exitCode)
        => EnvironmentPrincipalCommandSupport.TryHandleValidationException(logger, ex, LogAmbiguousMatch, out exitCode);

    public static async Task<Guid> ResolveEnvironmentIdAsync(string? profileName, CancellationToken ct)
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var context = await resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        return await ResolveEnvironmentIdAsync(context, ct).ConfigureAwait(false);
    }

    public static async Task<Guid> ResolveEnvironmentIdAsync(ResolvedProfileContext context, CancellationToken ct)
    {
        if (context.Connection.EnvironmentId.HasValue)
            return context.Connection.EnvironmentId.Value;

        if (string.IsNullOrWhiteSpace(context.Connection.EnvironmentUrl)
            || !Uri.TryCreate(context.Connection.EnvironmentUrl, UriKind.Absolute, out var environmentUrl))
        {
            throw new InvalidOperationException(
                $"Connection '{context.Connection.Id}' has no EnvironmentUrl or EnvironmentId.");
        }

        var service = TxcServices.Get<IEnvironmentManagementService>();
        var environment = (await service.ListAsync(
            context.Profile?.Id,
            credentialId: null,
            cloud: null,
            ct).ConfigureAwait(false))
            .SingleOrDefault(candidate => UrlEquals(candidate.EnvironmentUrl, environmentUrl));

        return environment?.EnvironmentId
            ?? throw new InvalidOperationException(
                $"Could not resolve Power Platform environment for URL '{context.Connection.EnvironmentUrl}'.");
    }

    /// <summary>
    /// Applies the environment admin role to the current authenticated
    /// caller via <see cref="IEnvironmentUserProvisioningService"/>.
    /// </summary>
    public static Task SelfElevateAsync(ResolvedProfileContext context, Guid environmentId, CancellationToken ct)
        => TxcServices.Get<IEnvironmentUserProvisioningService>()
            .SelfElevateAsync(context.Connection, context.Credential, environmentId, ct);

    private static bool UrlEquals(Uri left, Uri right)
        => NormalizeEnvironmentUrl(left).AbsoluteUri.Equals(
            NormalizeEnvironmentUrl(right).AbsoluteUri,
            StringComparison.OrdinalIgnoreCase);

    private static Uri NormalizeEnvironmentUrl(Uri uri)
        => new(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
}
