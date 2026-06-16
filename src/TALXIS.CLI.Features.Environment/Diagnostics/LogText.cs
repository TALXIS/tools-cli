namespace TALXIS.CLI.Features.Environment.Diagnostics;

/// <summary>
/// Shared text helpers for the environment-log table renderers. Keeps cell
/// padding and message trimming in one place instead of being copy-pasted into
/// each leaf command's text renderer.
/// </summary>
internal static class LogText
{
    /// <summary>
    /// Fits <paramref name="value"/> to exactly <paramref name="width"/> columns:
    /// pads short values, and ellipsis-truncates long ones.
    /// </summary>
    public static string Fit(string? value, int width)
    {
        var v = value ?? string.Empty;
        return v.Length > width ? v[..(width - 1)] + "…" : v.PadRight(width);
    }

    /// <summary>
    /// Collapses a (possibly multi-line) value to a single trimmed line capped at
    /// <paramref name="max"/> characters. Returns an empty string for null/blank input.
    /// </summary>
    public static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var oneLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return oneLine.Length > max ? oneLine[..(max - 1)] + "…" : oneLine;
    }
}
