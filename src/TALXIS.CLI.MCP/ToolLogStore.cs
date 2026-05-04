using System.Text.Json;
using System.Text.Json.Serialization;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.MCP;

/// <summary>
/// In-memory store for failed tool diagnostics, exposed as MCP resources.
/// Entries are keyed by a unique run ID and evicted FIFO when the store exceeds capacity.
/// </summary>
internal sealed class ToolLogStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LogEntry> _logs = [];
    private readonly Queue<string> _order = [];
    private readonly int _maxEntries;

    /// <summary>URI scheme prefix for failure-detail resources.</summary>
    internal const string UriScheme = "txc://logs/";

    public ToolLogStore(int maxEntries = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxEntries = maxEntries;
    }

    /// <summary>
    /// Stores failed tool diagnostics and returns the resource URI.
    /// </summary>
    public string StoreFailure(string toolName, int exitCode, string? primaryText, string? errorSummary, string? fullLog)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var uri = $"{UriScheme}{toolName}/{runId}";
        var entry = new LogEntry(
            ToolName: toolName,
            ExitCode: exitCode,
            PrimaryText: Normalize(primaryText),
            ErrorSummary: Normalize(errorSummary),
            FullLog: Normalize(fullLog),
            Timestamp: DateTimeOffset.UtcNow);

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
        string? FullLog,
        DateTimeOffset Timestamp)
    {
        public string Kind => "tool-failure-details";

        public string Summary => FirstNonEmpty(
            PrimaryText,
            ErrorSummary,
            $"Tool '{ToolName}' failed with exit code {ExitCode}.")!;

        [JsonIgnore]
        public bool HasFullLog => !string.IsNullOrWhiteSpace(FullLog);

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
