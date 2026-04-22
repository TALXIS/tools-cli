using System.Globalization;
using TALXIS.CLI.Config.Model;

namespace TALXIS.CLI.Config.Commands.Setting;

/// <summary>
/// Whitelist of tool-wide setting keys exposed via <c>txc config setting</c>.
/// Kept intentionally narrow in v1 — no free-form key-value soup. Each
/// descriptor knows how to read, write, and validate its slot on
/// <see cref="GlobalConfig"/> so the CLI verbs stay small and uniform.
/// </summary>
internal static class SettingRegistry
{
    public static readonly IReadOnlyList<SettingDescriptor> All = new SettingDescriptor[]
    {
        new(
            "log.level",
            "Minimum log level for diagnostic output (stderr).",
            new[] { "trace", "debug", "information", "warning", "error", "critical", "none" },
            g => g.Log.Level,
            (g, v) => g.Log.Level = v),
        new(
            "log.format",
            "Diagnostic log rendering format.",
            new[] { "plain", "json" },
            g => g.Log.Format,
            (g, v) => g.Log.Format = v),
        new(
            "telemetry.enabled",
            "Whether anonymous usage telemetry is enabled.",
            null,
            g => g.Telemetry.Enabled ? "true" : "false",
            (g, v) => g.Telemetry.Enabled = ParseBool(v)),
    };

    public static SettingDescriptor? Find(string key)
        => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string NormalizeValue(SettingDescriptor descriptor, string raw)
    {
        if (descriptor.Key == "telemetry.enabled")
            return ParseBool(raw) ? "true" : "false";

        var lowered = raw.Trim().ToLowerInvariant();
        if (descriptor.AllowedValues is { } allowed)
        {
            var match = allowed.FirstOrDefault(a =>
                string.Equals(a, lowered, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new ArgumentException(
                    $"Invalid value '{raw}' for '{descriptor.Key}'. Allowed: {string.Join(", ", allowed)}.");
            return match;
        }
        return lowered;
    }

    private static bool ParseBool(string raw)
    {
        var v = raw.Trim().ToLowerInvariant();
        return v switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new ArgumentException(
                $"Invalid boolean value '{raw}'. Allowed: true, false."),
        };
    }
}

internal sealed record SettingDescriptor(
    string Key,
    string Description,
    IReadOnlyList<string>? AllowedValues,
    Func<GlobalConfig, string> Read,
    Action<GlobalConfig, string> Write);
