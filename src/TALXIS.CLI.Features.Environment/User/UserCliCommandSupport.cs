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
    {
        filter = DataverseSecurityPrincipalStateFilter.Enabled;
        var selected = (enabled ? 1 : 0) + (disabled ? 1 : 0) + (all ? 1 : 0);
        if (selected > 1)
        {
            logger.LogError("Specify at most one of --enabled, --disabled, or --all.");
            return false;
        }

        if (disabled)
            filter = DataverseSecurityPrincipalStateFilter.Disabled;
        else if (all)
            filter = DataverseSecurityPrincipalStateFilter.All;

        return true;
    }

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
                $"{Truncate(row.FullName ?? string.Empty, nameWidth).PadRight(nameWidth)} | " +
                $"{Truncate(row.UserPrincipalName ?? string.Empty, upnWidth).PadRight(upnWidth)} | " +
                $"{Truncate(row.PrimaryEmailAddress ?? string.Empty, emailWidth).PadRight(emailWidth)} | " +
                $"{(row.IsDisabled ? "disabled" : "enabled").PadRight(stateWidth)} | " +
                $"{Truncate(row.BusinessUnitName ?? string.Empty, buWidth).PadRight(buWidth)} | {row.Id}");
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
                $"{Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{Truncate(row.BusinessUnitName ?? string.Empty, buWidth).PadRight(buWidth)} | {row.Id}");
        }
    }

    public static bool TryParseRoleIdentifiers(
        string? csv,
        ILogger logger,
        out IReadOnlyList<string> roles)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            roles = Array.Empty<string>();
            return true;
        }

        var parsed = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (parsed.Length == 0)
        {
            logger.LogError("--role must contain at least one role name or GUID when specified.");
            roles = Array.Empty<string>();
            return false;
        }

        roles = parsed;
        return true;
    }

    public static bool TryHandleValidationException(ILogger logger, Exception ex, out int exitCode)
    {
        if (ex is DataverseAmbiguousMatchException ambiguous)
        {
            LogAmbiguousMatch(logger, ambiguous);
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

    public static async Task SelfElevateAsync(ResolvedProfileContext context, Guid environmentId, CancellationToken ct)
    {
        var clientType = Type.GetType(
            "TALXIS.CLI.Platform.PowerPlatform.Control.EnvironmentSettingsClient, TALXIS.CLI.Platform.PowerPlatform.Control",
            throwOnError: true)!;
        var client = TxcServices.Provider?.GetService(clientType)
            ?? throw new InvalidOperationException("Environment self-elevation service is not registered.");
        var method = clientType.GetMethod(
            "SelfElevateAsync",
            new[] { typeof(Connection), typeof(Credential), typeof(Guid), typeof(CancellationToken) })
            ?? throw new InvalidOperationException("Environment self-elevation method is unavailable.");

        var task = method.Invoke(client, new object[] { context.Connection, context.Credential, environmentId, ct }) as Task
            ?? throw new InvalidOperationException("Environment self-elevation invocation did not return a task.");
        await task.ConfigureAwait(false);
    }

    private static bool UrlEquals(Uri left, Uri right)
        => NormalizeEnvironmentUrl(left).AbsoluteUri.Equals(
            NormalizeEnvironmentUrl(right).AbsoluteUri,
            StringComparison.OrdinalIgnoreCase);

    private static Uri NormalizeEnvironmentUrl(Uri uri)
        => new(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");

    private static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
