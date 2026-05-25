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

    /// <summary>
    /// Applies stored log settings as environment variables so that
    /// <c>TxcLoggerFactory</c> (which reads <c>TXC_LOG_LEVEL</c> and
    /// <c>TXC_LOG_FORMAT</c>) picks them up. Only sets vars that are
    /// not already overridden by the environment (env takes priority).
    /// Called once during telemetry bootstrap after config is loaded.
    /// </summary>
    public void ApplyAsEnvironmentDefaults()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TXC_LOG_LEVEL"))
            && !string.Equals(Level, "information", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("TXC_LOG_LEVEL", Level);
        }

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TXC_LOG_FORMAT"))
            && !string.Equals(Format, "plain", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("TXC_LOG_FORMAT", Format);
        }
    }
}

public sealed class TelemetrySettings
{
    /// <summary>
    /// Optional user-provided App Insights connection string override.
    /// When null, the built-in default (embedded at Release build time) is used.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// When true, telemetry collection is disabled entirely. No data is sent.
    /// Can also be set via the <c>TXC_TELEMETRY_OPTOUT</c> environment variable.
    /// </summary>
    public bool OptOut { get; set; }

    /// <summary>
    /// Tracks whether the first-run telemetry notice has been shown.
    /// Set automatically after the notice is displayed; not user-facing.
    /// </summary>
    public bool NoticeShown { get; set; }
}
