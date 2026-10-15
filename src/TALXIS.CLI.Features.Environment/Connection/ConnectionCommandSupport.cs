using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Features.Environment.Connection;

/// <summary>Option parsing and rendering shared by the connection commands.</summary>
internal static class ConnectionCommandSupport
{
    /// <summary>
    /// Parses repeated <c>name=value</c> pairs. The first '=' separates the name,
    /// so values may contain '='.
    /// </summary>
    public static bool TryParsePairs(
        IEnumerable<string>? items, string optionName, out Dictionary<string, string> result, out string? error)
    {
        result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        foreach (var item in items ?? [])
        {
            var split = item.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0)
            {
                error = $"{optionName} expects name=value, got '{item}'.";
                return false;
            }

            var name = item[..split].Trim();
            if (result.ContainsKey(name))
            {
                error = $"{optionName} '{name}' was given more than once.";
                return false;
            }

            result[name] = item[(split + 1)..];
        }

        return true;
    }

    /// <summary>Accepts <c>can-view</c>, <c>CanView</c> and <c>can_view</c>.</summary>
    public static bool TryParseRole(string? text, out ConnectionPermissionRole role)
    {
        role = ConnectionPermissionRole.CanView;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        switch (Normalize(text))
        {
            case "canview":
                role = ConnectionPermissionRole.CanView;
                return true;
            case "canviewwithshare":
                role = ConnectionPermissionRole.CanViewWithShare;
                return true;
            case "canedit":
                role = ConnectionPermissionRole.CanEdit;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The platform role in the CLI's kebab-case vocabulary (<c>CanView</c> becomes <c>can-view</c>).</summary>
    public static string FormatRole(string platformRole) => platformRole switch
    {
        "CanView" => "can-view",
        "CanViewWithShare" => "can-view-with-share",
        "CanEdit" => "can-edit",
        _ => platformRole.ToLowerInvariant(),
    };

    /// <summary>Exactly one of --user, --service-principal and --group.</summary>
    public static bool TryBuildPrincipal(
        string? user, string? servicePrincipal, string? group, out ConnectionPrincipalSpec? principal, out string? error)
    {
        principal = null;
        error = null;

        var given = new[] { user, servicePrincipal, group }.Count(v => !string.IsNullOrWhiteSpace(v));
        if (given != 1)
        {
            error = "Pass exactly one of --user, --service-principal or --group.";
            return false;
        }

        principal = !string.IsNullOrWhiteSpace(user)
            ? new ConnectionPrincipalSpec(ConnectionPrincipalKind.User, user)
            : !string.IsNullOrWhiteSpace(servicePrincipal)
                ? new ConnectionPrincipalSpec(ConnectionPrincipalKind.ServicePrincipal, servicePrincipal)
                : new ConnectionPrincipalSpec(ConnectionPrincipalKind.Group, group!);
        return true;
    }

    public static string Fit(string? value, int width)
    {
        var text = value ?? string.Empty;
        return text.Length > width ? text[..(width - 1)] + "." : text;
    }

    private static string Normalize(string text)
        => text.Replace("-", string.Empty, StringComparison.Ordinal)
               .Replace("_", string.Empty, StringComparison.Ordinal)
               .Trim()
               .ToLowerInvariant();
}
