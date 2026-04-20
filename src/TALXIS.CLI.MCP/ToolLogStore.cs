namespace TALXIS.CLI.MCP;

/// <summary>
/// In-memory store for tool execution logs, exposed as MCP resources.
/// Logs are keyed by a unique run ID and evicted FIFO when the store exceeds capacity.
/// </summary>
internal sealed class ToolLogStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LogEntry> _logs = [];
    private readonly Queue<string> _order = [];
    private readonly int _maxEntries;

    /// <summary>URI scheme prefix for tool log resources.</summary>
    internal const string UriScheme = "txc://logs/";

    public ToolLogStore(int maxEntries = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxEntries = maxEntries;
    }

    /// <summary>
    /// Stores a tool execution log and returns the resource URI.
    /// </summary>
    public string Store(string toolName, string fullLog, string errorSummary, bool isError)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var uri = $"{UriScheme}{toolName}/{runId}";
        var entry = new LogEntry(toolName, fullLog, errorSummary, isError, DateTimeOffset.UtcNow);

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

    internal sealed record LogEntry(
        string ToolName,
        string FullLog,
        string ErrorSummary,
        bool IsError,
        DateTimeOffset Timestamp);
}
