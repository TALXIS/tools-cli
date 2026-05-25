using System.Diagnostics;
using TALXIS.CLI.Abstractions;

namespace TALXIS.CLI.Core.Telemetry;

/// <summary>
/// Creates and manages the Activity span for a command/tool execution.
/// Host-agnostic — CLI, MCP, and future REST API hosts all use this to create
/// telemetry spans with consistent structure. The host passes its <paramref name="entryPoint"/>
/// identifier; the scope handles span creation, W3C traceparent restoration, and initial tagging.
/// </summary>
public sealed class CommandActivityScope : IDisposable
{

    private int _exitCode;

    /// <summary>The Activity span for this command execution. Null when no listener is registered.</summary>
    public Activity? Activity { get; }

    /// <summary>
    /// Creates a new command execution span.
    /// </summary>
    /// <param name="operationName">Command class name (CLI) or tool name (MCP).</param>
    /// <param name="entryPoint">Host identifier: <c>"cli"</c>, <c>"mcp"</c>, <c>"api"</c>.</param>
    /// <param name="parentTraceparent">Optional W3C traceparent from <c>TXC_TRACEPARENT</c> env var for cross-process correlation.</param>
    public CommandActivityScope(string operationName, string entryPoint, string? parentTraceparent = null)
    {
        var parentContext = ParseTraceparent(parentTraceparent);
        Activity = parentContext.HasValue
            ? TxcActivitySource.Instance.StartActivity(operationName, ActivityKind.Server, parentContext.Value)
            : TxcActivitySource.Instance.StartActivity(operationName, ActivityKind.Server);

        Activity?.SetTag(TxcTelemetryTags.Command, operationName);
        Activity?.SetTag(TxcTelemetryTags.EntryPoint, entryPoint);
        Activity?.SetTag(TxcTelemetryTags.Version, TxcActivitySource.Instance.Version);
    }

    /// <summary>
    /// Records the exit code on the span. Called once before disposal.
    /// Non-zero exit codes also set the span error status and default
    /// <c>txc.error_kind</c> to <c>"validation"</c> when no catch block
    /// has already classified the error via <see cref="SetError"/>.
    /// </summary>
    public void SetExitCode(int exitCode)
    {
        _exitCode = exitCode;
        Activity?.SetTag(TxcTelemetryTags.ExitCode, exitCode);
        if (exitCode != 0)
        {
            Activity?.SetStatus(ActivityStatusCode.Error, $"Exit code {exitCode}");

            // Default error kind for non-exception failures (e.g. Logger.LogError + return ExitError).
            // Exception-based failures always call SetError() first, which already sets ErrorKind.
            if (Activity?.GetTagItem(TxcTelemetryTags.ErrorKind) is null)
                Activity?.SetTag(TxcTelemetryTags.ErrorKind, "validation");
        }
    }

    /// <summary>
    /// Records a classified error on the span (exit code + error kind + optional message).
    /// Called from catch blocks in <see cref="TxcLeafCommand"/>.
    /// The <paramref name="errorMessage"/> appears as <c>txc.error_message</c> in App Insights —
    /// visible at a glance without drilling into exception events.
    /// </summary>
    public void SetError(int exitCode, string errorKind, string? errorMessage = null)
    {
        _exitCode = exitCode;
        Activity?.SetTag(TxcTelemetryTags.ExitCode, exitCode);
        Activity?.SetTag(TxcTelemetryTags.ErrorKind, errorKind);
        if (!string.IsNullOrWhiteSpace(errorMessage))
            Activity?.SetTag(TxcTelemetryTags.ErrorMessage, errorMessage);
    }

    public void Dispose() => Activity?.Dispose();

    /// <summary>
    /// Parses a W3C traceparent string (<c>00-{traceId}-{spanId}-{flags}</c>)
    /// into an <see cref="ActivityContext"/> for cross-process correlation.
    /// Returns null for standalone CLI usage (no parent context).
    /// </summary>
    private static ActivityContext? ParseTraceparent(string? traceparent)
    {
        if (string.IsNullOrEmpty(traceparent))
            return null;

        try
        {
            var parts = traceparent.Split('-');
            if (parts.Length < 4 || parts[1].Length != 32 || parts[2].Length != 16)
                return null;

            var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
            var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
            var flags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
            return new ActivityContext(traceId, spanId, flags, isRemote: true);
        }
        catch
        {
            return null;
        }
    }
}
