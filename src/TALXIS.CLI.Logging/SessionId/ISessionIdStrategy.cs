namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// A strategy that attempts to resolve a terminal/client session identifier
/// from the current process environment.
/// </summary>
public interface ISessionIdStrategy
{
    /// <summary>
    /// Human-readable label identifying this strategy (e.g. "copilot", "claude-code", "terminal").
    /// Recorded as <c>txc.session_id.source</c> in telemetry so we can see which
    /// strategy provided the session ID in App Insights.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Attempts to resolve a session ID. Returns null if this strategy does not apply
    /// to the current environment (e.g. the expected env var is not set).
    /// </summary>
    string? TryResolve();
}
