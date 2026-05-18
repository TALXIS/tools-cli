using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Logger provider that bridges <see cref="ILogger"/> events to the current
/// <see cref="Activity"/> (OTel span). This makes ILogger the single source
/// of truth for all diagnostics — console, MCP, and App Insights all consume
/// the same events without command code touching <see cref="Activity"/> directly.
///
/// <para>Behavior:</para>
/// <list type="bullet">
///   <item>Structured properties prefixed with <c>txc.</c> are mapped to Activity tags.</item>
///   <item><see cref="LogLevel.Error"/> and <see cref="LogLevel.Critical"/> events with
///         an exception produce OTel exception events (→ App Insights <c>exceptions</c> table).</item>
///   <item>All text is redacted via <see cref="LogRedactionFilter"/> before reaching the Activity.</item>
///   <item>When no Activity is current, all operations are no-ops (zero overhead).</item>
/// </list>
/// </summary>
public sealed class TxcTelemetryLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TxcTelemetryLogger();

    public void Dispose() { }
}

internal sealed class TxcTelemetryLogger : ILogger
{
    /// <summary>
    /// Thread-local scope properties set via <see cref="BeginScope{TState}"/>.
    /// Used to pass telemetry-only properties (txc.*) without polluting the
    /// formatted log message that human-facing providers render.
    /// </summary>
    private static readonly AsyncLocal<Dictionary<string, object>?> _scopeProperties = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        // Accept Dictionary<string, object> scopes containing txc.* properties
        if (state is Dictionary<string, object> dict)
        {
            _scopeProperties.Value = dict;
            return new ScopeDisposable();
        }
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && Activity.Current != null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var activity = Activity.Current;
        if (activity == null)
            return;

        // Map well-known structured properties from message template to Activity tags
        if (state is IReadOnlyList<KeyValuePair<string, object?>> properties)
        {
            foreach (var kvp in properties)
            {
                if (kvp.Key.StartsWith("txc.", StringComparison.Ordinal) && kvp.Value != null)
                {
                    activity.SetTag(kvp.Key, kvp.Value.ToString());
                }
            }
        }

        // Map scope properties (set via BeginScope) to Activity tags —
        // these are telemetry-only properties that don't appear in the
        // formatted log message visible to human-facing providers.
        if (_scopeProperties.Value is { } scope)
        {
            foreach (var kvp in scope)
            {
                if (kvp.Key.StartsWith("txc.", StringComparison.Ordinal))
                    activity.SetTag(kvp.Key, kvp.Value.ToString());
            }
        }

        // Record exceptions with redaction for Error and Critical levels.
        // Uses OTel semantic conventions (exception.type, exception.message,
        // exception.stacktrace) so the Azure Monitor exporter creates rows
        // in the App Insights 'exceptions' table with full stack traces.
        if (exception != null && logLevel >= LogLevel.Error)
        {
            activity.AddEvent(new ActivityEvent("exception",
                tags: new ActivityTagsCollection
                {
                    { "exception.type", exception.GetType().FullName },
                    { "exception.message", LogRedactionFilter.Redact(exception.Message) },
                    { "exception.stacktrace", LogRedactionFilter.Redact(exception.ToString()) },
                }));
        }

        // Set span error status from log level
        if (logLevel >= LogLevel.Error)
        {
            var message = LogRedactionFilter.Redact(formatter(state, null));
            activity.SetStatus(ActivityStatusCode.Error, message);
        }
    }

    private sealed class ScopeDisposable : IDisposable
    {
        public void Dispose() => _scopeProperties.Value = null;
    }
}
