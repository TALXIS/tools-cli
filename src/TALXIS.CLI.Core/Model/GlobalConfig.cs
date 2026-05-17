namespace TALXIS.CLI.Core.Model;

/// <summary>
/// Root of <c>${TXC_CONFIG_DIR:-~/.txc}/config.json</c>. Holds the active-profile
/// pointer and tool-wide preferences. Kept intentionally narrow.
/// </summary>
public sealed class GlobalConfig
{
    public string? ActiveProfile { get; set; }
    public LogSettings Log { get; set; } = new();
    public TelemetrySettings Telemetry { get; set; } = new();
}

public sealed class LogSettings
{
    public string Level { get; set; } = "information";
    public string Format { get; set; } = "plain";
}

public sealed class TelemetrySettings
{
    /// <summary>
    /// Telemetry is on by default for all published builds.
    /// Can be disabled via <c>txc config setting set telemetry.enabled false</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional user-provided App Insights connection string override.
    /// When null, the built-in default (embedded at Release build time) is used.
    /// </summary>
    public string? ConnectionString { get; set; }
}
