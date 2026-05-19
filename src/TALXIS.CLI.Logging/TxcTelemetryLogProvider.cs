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
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

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
}
