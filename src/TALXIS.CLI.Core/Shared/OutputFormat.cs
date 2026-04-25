namespace TALXIS.CLI.Core;

/// <summary>
/// Determines how command result data is formatted on stdout.
/// </summary>
public enum OutputFormat
{
    /// <summary>Machine-readable JSON (default for pipes and automation).</summary>
    Json = 0,

    /// <summary>Human-friendly text tables (default for interactive terminals).</summary>
    Text = 1
}
