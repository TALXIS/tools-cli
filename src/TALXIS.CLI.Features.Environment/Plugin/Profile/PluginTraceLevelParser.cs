using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Plugin.Profile;

/// <summary>
/// Parses the <c>--level</c> value for the plugin trace commands into a
/// <see cref="PluginTraceLevel"/>. Pure and public so it stays unit-testable.
/// </summary>
public static class PluginTraceLevelParser
{
    /// <summary>
    /// Parses a <c>--level</c> value. An empty/whitespace value yields
    /// <paramref name="default"/>.
    /// </summary>
    public static bool TryParse(
        string? value,
        PluginTraceLevel @default,
        out PluginTraceLevel level,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            level = @default;
            error = null;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "all":
                level = PluginTraceLevel.All;
                break;
            case "exception":
            case "exceptions":
                level = PluginTraceLevel.Exception;
                break;
            case "off":
            case "none":
                level = PluginTraceLevel.Off;
                break;
            default:
                level = @default;
                error = $"Invalid --level value '{value}'. Expected: all, exception, or off.";
                return false;
        }

        error = null;
        return true;
    }
}
