using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Logger provider that appends redacted JSON log lines to a session-scoped file
/// in the OS temp directory. Enables local troubleshooting without App Insights access.
///
/// File path: <c>$TMPDIR/txc/logs/session-{sessionId}.jsonl</c>
///
/// Each line includes the current operation ID (trace ID) for easy filtering by operation.
/// The OS temp cleanup policy handles retention — no manual rotation needed.
/// </summary>
public sealed class JsonFileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly object _writeLock = new();
    private StreamWriter? _writer;
    private bool _disposed;

    public JsonFileLoggerProvider(string sessionId)
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "txc", "logs");
        Directory.CreateDirectory(tempBase);
        _filePath = Path.Combine(tempBase, $"session-{sessionId}.jsonl");
    }

    /// <summary>
    /// The full path to the session log file. Shown in support escalation messages.
    /// </summary>
    public string FilePath => _filePath;

    public ILogger CreateLogger(string categoryName) => new JsonFileLogger(categoryName, this);

    internal void WriteLine(string line)
    {
        if (_disposed) return;
        lock (_writeLock)
        {
            if (_disposed) return;
            _writer ??= new StreamWriter(new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_writeLock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

internal sealed class JsonFileLogger : ILogger
{
    private readonly string _category;
    private readonly JsonFileLoggerProvider _provider;

    public JsonFileLogger(string category, JsonFileLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
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
            message = $"{message} {exception}";

        message = LogRedactionFilter.Redact(message) ?? string.Empty;

        Dictionary<string, object?>? data = null;
        if (state is IReadOnlyList<KeyValuePair<string, object?>> properties)
        {
            foreach (var kvp in properties)
            {
                if (kvp.Key == "{OriginalFormat}")
                    continue;
                data ??= new Dictionary<string, object?>();
                data[kvp.Key] = kvp.Value is string s ? LogRedactionFilter.Redact(s) : kvp.Value;
            }
        }

        // Include operation ID for filtering by specific tool call
        var operationId = Activity.Current?.TraceId.ToHexString();

        var logLine = new JsonLogLine
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Level = logLevel.ToString(),
            Category = _category,
            Message = message,
            Data = data
        };

        // Append operation ID to data if present
        if (operationId != null)
        {
            logLine.Data ??= new Dictionary<string, object?>();
            logLine.Data["operationId"] = operationId;
        }

        try
        {
            _provider.WriteLine(logLine.Serialize());
        }
        catch (Exception)
        {
            // File logging must never crash the CLI
        }
    }
}
