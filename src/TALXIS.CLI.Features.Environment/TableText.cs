namespace TALXIS.CLI.Features.Environment;

/// <summary>Text helpers for the fixed-width tables the list commands print.</summary>
internal static class TableText
{
    /// <summary>
    /// Truncates a display value to fit within a fixed-width table column,
    /// appending a trailing "." marker when truncation occurs.
    /// </summary>
    internal static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
