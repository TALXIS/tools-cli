using System.Diagnostics;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Formats support escalation information for error output.
/// Called from both CLI and MCP error paths to produce a consistent message
/// directing users to report issues with their session and operation IDs.
///
/// Lives in the Logging project because it depends on telemetry state
/// (session ID, Activity). Consumers that cannot reference Logging directly
/// (e.g. Core) should not call this — the entry-point layer handles it.
/// </summary>
public static class TxcSupportInfo
{
    private const string IssuesUrl = "https://github.com/TALXIS/tools-cli/issues";

    private static string? _sessionId;
    private static string? _logFilePath;

    /// <summary>
    /// Called once at telemetry init to make the session ID available for error output.
    /// </summary>
    public static void SetSessionId(string sessionId) => _sessionId = sessionId;

    /// <summary>
    /// Called once when the file logger provider is created.
    /// </summary>
    public static void SetLogFilePath(string path) => _logFilePath = path;

    /// <summary>
    /// Formats a support escalation block with session ID, operation ID, log file path, and GitHub link.
    /// Returns an empty string if no telemetry context is available (e.g. Debug builds).
    /// </summary>
    public static string FormatEscalation()
    {
        var operationId = Activity.Current?.TraceId.ToHexString();

        if (_sessionId == null && operationId == null)
            return string.Empty;

        var lines = $"If this is unexpected, report at {IssuesUrl}{Environment.NewLine}" +
                    $"  Session: {_sessionId ?? "unknown"}{Environment.NewLine}" +
                    $"  Operation: {operationId ?? "unknown"}";

        if (_logFilePath != null)
            lines += $"{Environment.NewLine}  Logs: {_logFilePath}";

        return lines;
    }
}
