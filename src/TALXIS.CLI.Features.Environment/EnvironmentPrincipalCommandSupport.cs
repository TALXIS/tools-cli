using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment;

/// <summary>
/// Shared command-support helpers reused by the <c>txc environment user</c>,
/// <c>txc environment app</c>, and <c>txc environment team</c> command
/// groups, all of which manage Dataverse security principals within an
/// environment. Mirrors <c>TenantPrincipalCommandSupport</c> on the tenant
/// side.
/// </summary>
internal static class EnvironmentPrincipalCommandSupport
{
    /// <summary>
    /// Resolves the mutually-exclusive <c>--enabled</c>/<c>--disabled</c>/<c>--all</c>
    /// list filter options shared by <c>environment user list</c> and
    /// <c>environment app list</c>.
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
    /// and app "create with roles" commands.
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
}
