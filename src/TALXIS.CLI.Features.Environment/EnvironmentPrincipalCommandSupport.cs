using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment;

/// <summary>
/// Shared command-support helpers reused by the <c>txc environment user</c>,
/// <c>txc environment service-principal</c>, and <c>txc environment team</c> command
/// groups, all of which manage Dataverse security principals within an
/// environment. Mirrors <c>TenantPrincipalCommandSupport</c> on the tenant
/// side.
/// </summary>
internal static class EnvironmentPrincipalCommandSupport
{
    /// <summary>
    /// Resolves the mutually-exclusive <c>--enabled</c>/<c>--disabled</c>/<c>--all</c>
    /// list filter options shared by <c>environment user list</c> and
    /// <c>environment service-principal list</c>.
    /// </summary>
    internal static bool TryResolveStateFilter(
        bool enabled,
        bool disabled,
        bool all,
        ILogger logger,
        out DataverseSecurityPrincipalStateFilter filter)
    {
        var selected = (enabled ? 1 : 0) + (disabled ? 1 : 0) + (all ? 1 : 0);
        if (selected > 1)
        {
            logger.LogError("Specify at most one of --enabled, --disabled, or --all.");
            filter = default;
            return false;
        }

        filter = disabled
            ? DataverseSecurityPrincipalStateFilter.Disabled
            : all
                ? DataverseSecurityPrincipalStateFilter.All
                : DataverseSecurityPrincipalStateFilter.Enabled;
        return true;
    }

    /// <summary>
    /// Parses a comma-separated <c>--role</c> option value into a
    /// deduplicated list of role names/GUIDs, shared by the environment user
    /// and service-principal "create with roles" commands.
    /// </summary>
    internal static bool TryParseRoleIdentifiers(
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

    /// <summary>
    /// Truncates a display value to fit within a fixed-width table column,
    /// appending a trailing "." marker when truncation occurs.
    /// </summary>
    internal static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;

    /// <summary>
    /// Matches an already-assigned role against a caller-supplied
    /// <c>--role</c> identifier, which may be either the role's GUID or its
    /// friendly name. Shared by the <c>role add</c> commands for
    /// <c>environment user</c>, <c>environment service-principal</c>, and
    /// <c>environment team</c> to consistently detect a no-op re-assignment.
    /// </summary>
    internal static bool IsRoleMatch(DataverseRoleRecord role, string roleNameOrGuid)
        => string.Equals(role.Id.ToString(), roleNameOrGuid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role.Name, roleNameOrGuid, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Shared exit-code mapping for the validation-style exceptions raised by
    /// Dataverse security-principal resolution/mutation (ambiguous friendly-name
    /// match, invalid argument, invalid operation). Callers supply a
    /// <paramref name="logAmbiguousMatch"/> delegate so each command group can
    /// keep its own candidate-listing format (e.g. <c>environment user</c> logs a
    /// bulleted list, <c>environment service-principal</c> logs a single "Candidate:" line per
    /// match) while sharing the exception-type dispatch and exit-code contract.
    /// </summary>
    internal static bool TryHandleValidationException(
        ILogger logger,
        Exception ex,
        Action<ILogger, DataverseAmbiguousMatchException> logAmbiguousMatch,
        out int exitCode)
    {
        if (ex is DataverseAmbiguousMatchException ambiguous)
        {
            logAmbiguousMatch(logger, ambiguous);
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
}
