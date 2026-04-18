using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Logger that writes structured JSON log lines to stderr.
/// Used when TXC_LOG_FORMAT=json (i.e., running under MCP server).
/// Each log entry is a single JSON line — newlines in messages are escaped.
/// </summary>
internal sealed class JsonStderrLogger : ILogger
{
    private readonly string _category;
    private readonly TextWriter _stderr;

    public JsonStderrLogger(string category, TextWriter stderr)
    {
        _category = category;
        _stderr = stderr;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        string message = formatter(state, exception);
        if (exception != null)
        {
            message = $"{message} {exception}";
        }

        Dictionary<string, object?>? data = null;
        int? progress = null;

        // Extract structured properties from the log state
        if (state is IReadOnlyList<KeyValuePair<string, object?>> properties)
        {
            foreach (var kvp in properties)
            {
                // Skip the "{OriginalFormat}" property — that's the message template
                if (kvp.Key == "{OriginalFormat}")
                    continue;

                if (kvp.Key == "Progress" && kvp.Value is int p)
                {
                    progress = p;
                    continue;
                }

                data ??= new Dictionary<string, object?>();
                data[kvp.Key] = kvp.Value;
            }
        }

        var logLine = new JsonLogLine
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Level = logLevel.ToString(),
            Category = _category,
            Message = message,
            Data = data,
            Progress = progress
        };

        _stderr.WriteLine(logLine.Serialize());
    }
}
