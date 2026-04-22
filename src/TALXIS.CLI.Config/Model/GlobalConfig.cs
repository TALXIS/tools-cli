namespace TALXIS.CLI.Config.Model;

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
    public bool Enabled { get; set; } = false;
}
