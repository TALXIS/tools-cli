using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Abstractions;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Logger provider that bridges <see cref="ILogger"/> events to the current
/// <see cref="Activity"/> (OTel span). This makes ILogger the single source
/// of truth for all diagnostics — console, MCP, and App Insights all consume
/// the same events without command code touching <see cref="Activity"/> directly.
///
/// <para>Behavior:</para>
/// <list type="bullet">
///   <item><see cref="LogLevel.Error"/> and <see cref="LogLevel.Critical"/> events with
///         an exception produce OTel exception events (→ App Insights <c>exceptions</c> table)
///         using semantic conventions (<c>exception.type</c>, <c>exception.message</c>,
///         <c>exception.stacktrace</c>).</item>
///   <item>All text is redacted via <see cref="LogRedactionFilter"/> before reaching the Activity.</item>
///   <item>When no Activity is current, all operations are no-ops (zero overhead).</item>
///   <item>Span error status is NOT set here — <c>CommandActivityScope.SetExitCode()</c>
///         is the sole authority to avoid conflicting status overwrites.</item>
/// </list>
/// </summary>
public sealed class TxcTelemetryLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TxcTelemetryLogger();

    public void Dispose() { }
}

internal sealed class TxcTelemetryLogger : ILogger
{
    private static readonly string[] CopiedActivityTagKeys =
    [
        "enduser.id",
        "enduser.name",
        "enduser.scope",
        "txc.environment_url",
        "txc.environment_name",
        "txc.error_message",
    ];

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
            var root = ExceptionHelpers.GetInnermostException(exception);
            var redactedErrorMessage = LogRedactionFilter.Redact(root.Message);
            SetErrorMessageIfMissing(activity, redactedErrorMessage);

            var eventTags = new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", LogRedactionFilter.Redact(exception.Message) },
                { "exception.stacktrace", LogRedactionFilter.Redact(exception.ToString()) },
            };
            CopySelectedActivityTags(activity, eventTags);
            activity.AddEvent(new ActivityEvent("exception",
                tags: eventTags));
        }

        // For Error/Critical log calls WITHOUT an exception object (e.g.,
        // WorkspaceValidateCliCommand logging per-file validation errors),
        // stamp the formatted message as txc.error_message on the span.
        // This provides at-a-glance context in App Insights even when there
        // is no exception event. Overwrites on each call — the last error
        // message wins, giving a representative sample.
        if (exception == null && logLevel >= LogLevel.Error)
        {
            var msg = formatter(state, null);
            if (!string.IsNullOrWhiteSpace(msg))
                SetErrorMessageIfMissing(activity, LogRedactionFilter.Redact(msg), overwrite: true);
        }

        // Note: span error status is NOT set here — CommandActivityScope.SetExitCode()
        // is the sole authority for span status. This avoids double-SetStatus where the
        // logger's descriptive message gets overwritten by the generic "Exit code N".
    }

    private static void CopySelectedActivityTags(Activity activity, ActivityTagsCollection target)
    {
        foreach (var tagKey in CopiedActivityTagKeys)
        {
            var value = activity.GetTagItem(tagKey);
            if (value != null)
                target.Add(tagKey, value);
        }
    }

    private static void SetErrorMessageIfMissing(Activity activity, string errorMessage, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return;

        if (!overwrite && activity.GetTagItem("txc.error_message") is not null)
            return;

        activity.SetTag("txc.error_message", errorMessage);
    }
}
