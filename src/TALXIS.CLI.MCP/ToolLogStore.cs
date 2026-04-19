using System.Collections.Concurrent;

namespace TALXIS.CLI.MCP;

/// <summary>
/// In-memory store for tool execution logs, exposed as MCP resources.
/// Logs are keyed by a unique run ID and evicted FIFO when the store exceeds capacity.
/// </summary>
internal sealed class ToolLogStore
{
    private readonly ConcurrentDictionary<string, LogEntry> _logs = new();
    private readonly ConcurrentQueue<string> _order = new();
    private readonly int _maxEntries;

    /// <summary>URI scheme prefix for tool log resources.</summary>
    internal const string UriScheme = "txc://logs/";

    public ToolLogStore(int maxEntries = 50)
    {
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

        _logs[uri] = entry;
        _order.Enqueue(uri);

        // Evict oldest entries
        while (_order.Count > _maxEntries && _order.TryDequeue(out var oldUri))
        {
            _logs.TryRemove(oldUri, out _);
        }

        return uri;
    }

    /// <summary>
    /// Tries to retrieve a log entry by URI.
    /// </summary>
    public bool TryGet(string uri, out LogEntry? entry)
    {
        return _logs.TryGetValue(uri, out entry);
    }

    /// <summary>
    /// Returns all available log entries (for resources/list).
    /// </summary>
    public IReadOnlyList<(string Uri, LogEntry Entry)> ListAll()
    {
        return _logs.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    internal sealed record LogEntry(
        string ToolName,
        string FullLog,
        string ErrorSummary,
        bool IsError,
        DateTimeOffset Timestamp);
}
