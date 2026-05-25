using System.Text.Json;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.MCP;

/// <summary>
/// In-memory store for tool execution logs, exposed as MCP resources.
/// Entries are keyed by a unique run ID and evicted FIFO when the store exceeds capacity.
/// </summary>
internal sealed class ToolLogStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LogEntry> _logs = [];
    private readonly Queue<string> _order = [];
    private readonly int _maxEntries;
    private readonly Func<string?> _sessionIdAccessor;

    /// <summary>URI scheme prefix for execution-log resources.</summary>
    internal const string UriScheme = "txc://logs/";

    /// <param name="sessionIdAccessor">Provides the current session ID. Injected to avoid
    /// static coupling to <c>TxcTelemetrySetup</c>.</param>
    /// <param name="maxEntries">Maximum number of log entries to retain (FIFO eviction).</param>
    public ToolLogStore(Func<string?> sessionIdAccessor, int maxEntries = 50)
    {
        _sessionIdAccessor = sessionIdAccessor;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxEntries = maxEntries;
    }

    /// <summary>
    /// Stores a tool execution log and returns the resource URI.
    /// The <paramref name="executionId"/> should be the root Activity trace id (= App Insights operation_Id)
    /// so the diagnostics URI, telemetry, and log store all share one canonical identity.
    /// Falls back to a random id when no Activity is active (e.g. telemetry disabled).
    /// </summary>
    public string Store(string toolName, int exitCode, string? primaryText, string? errorSummary,
        IReadOnlyList<RedactedLogEntry>? logEntries, string? executionId = null)
    {
        executionId ??= Guid.NewGuid().ToString("N");
        var uri = $"{UriScheme}{executionId}";
        var sessionId = _sessionIdAccessor();
        var entry = new LogEntry(
            ToolName: toolName,
            ExitCode: exitCode,
            PrimaryText: Normalize(primaryText),
            ErrorSummary: Normalize(errorSummary),
            LogEntries: logEntries ?? [],
            Timestamp: DateTimeOffset.UtcNow,
            SessionId: sessionId,
            OperationId: executionId);

        lock (_sync)
        {
            _logs[uri] = entry;
            _order.Enqueue(uri);

            while (_order.Count > _maxEntries)
            {
                var oldUri = _order.Dequeue();
                _logs.Remove(oldUri);
            }
        }

        return uri;
    }

    /// <summary>
    /// Tries to retrieve a log entry by URI.
    /// </summary>
    public bool TryGet(string uri, out LogEntry? entry)
    {
        lock (_sync)
        {
            return _logs.TryGetValue(uri, out entry);
        }
    }

    /// <summary>
    /// Returns all available log entries (for resources/list).
    /// </summary>
    public IReadOnlyList<(string Uri, LogEntry Entry)> ListAll()
    {
        lock (_sync)
        {
            return _logs.Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal sealed record LogEntry(
        string ToolName,
        int ExitCode,
        string? PrimaryText,
        string? ErrorSummary,
        IReadOnlyList<RedactedLogEntry> LogEntries,
        DateTimeOffset Timestamp,
        string? SessionId = null,
        string? OperationId = null)
    {
        public string Kind => "tool-execution-log";

        public string Summary => FirstNonEmpty(
            PrimaryText,
            ErrorSummary,
            ExitCode == 0
                ? $"Tool '{ToolName}' completed successfully."
                : $"Tool '{ToolName}' failed with exit code {ExitCode}.")!;

        public string ToJson() => JsonSerializer.Serialize(this, TxcOutputJsonOptions.Default);

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
