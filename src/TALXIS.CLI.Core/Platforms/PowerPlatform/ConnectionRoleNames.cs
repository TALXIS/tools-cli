namespace TALXIS.CLI.Core.Platforms.PowerPlatform;

/// <summary>
/// The names of a <see cref="ConnectionPermissionRole"/> in the CLI's kebab-case
/// vocabulary and in the platform's own. One table serves both directions.
/// </summary>
public static class ConnectionRoleNames
{
    private static readonly (ConnectionPermissionRole Role, string Platform, string Cli)[] Table =
    [
        (ConnectionPermissionRole.CanView, "CanView", "can-view"),
        (ConnectionPermissionRole.CanViewWithShare, "CanViewWithShare", "can-view-with-share"),
        (ConnectionPermissionRole.CanEdit, "CanEdit", "can-edit"),
        (ConnectionPermissionRole.Owner, "Owner", "owner"),
    ];

    /// <summary>The CLI name, for example <c>can-view-with-share</c>.</summary>
    public static string ToCli(ConnectionPermissionRole role) => Find(role).Cli;

    /// <summary>
    /// Parses <c>can-view</c>, <c>can-view-with-share</c> or <c>can-edit</c>: the
    /// roles that can be granted. <c>owner</c> is not accepted.
    /// </summary>
    public static bool TryParseCli(string? text, out ConnectionPermissionRole role)
    {
        foreach (var entry in Table)
        {
            if (entry.Role != ConnectionPermissionRole.Owner
                && string.Equals(entry.Cli, text, StringComparison.Ordinal))
            {
                role = entry.Role;
                return true;
            }
        }

        role = default;
        return false;
    }

    internal static string ToPlatform(ConnectionPermissionRole role) => Find(role).Platform;

    internal static bool TryFromPlatform(string? text, out ConnectionPermissionRole role)
    {
        foreach (var entry in Table)
        {
            if (string.Equals(entry.Platform, text, StringComparison.OrdinalIgnoreCase))
            {
                role = entry.Role;
                return true;
            }
        }

        role = default;
        return false;
    }

    private static (ConnectionPermissionRole Role, string Platform, string Cli) Find(ConnectionPermissionRole role)
    {
        foreach (var entry in Table)
        {
            if (entry.Role == role)
                return entry;
        }

        throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown connection permission role.");
    }
}
