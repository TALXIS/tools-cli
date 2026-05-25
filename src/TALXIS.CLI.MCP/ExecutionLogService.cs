using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.Core;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Handles execution log queries and MCP resource operations.
/// Separated from <see cref="McpToolResultFactory"/> (which builds tool results)
/// to follow Single Responsibility — this class owns log retrieval and filtering,
/// the factory owns result construction.
/// </summary>
internal sealed class ExecutionLogService
{
    private readonly ToolLogStore _toolLogStore;

    public ExecutionLogService(ToolLogStore toolLogStore)
    {
        _toolLogStore = toolLogStore;
    }

    /// <summary>
    /// Returns all execution log entries as MCP resources (for resources/list).
    /// </summary>
    public List<Resource> ListResources()
    {
        return _toolLogStore.ListAll().Select(e => new Resource
        {
            Uri = e.Uri,
            Name = $"Execution log: {e.Entry.ToolName}",
            Description = BuildResourceDescription(e.Entry),
            MimeType = "application/json"
        }).ToList();
    }

    /// <summary>
    /// Reads a single execution log resource by URI (for resources/read).
    /// </summary>
    public ReadResourceResult ReadResource(string uri)
    {
        if (!_toolLogStore.TryGet(uri, out var entry) || entry is null)
        {
            throw new McpException($"Resource not found: {uri}");
        }

        return new ReadResourceResult
        {
            Contents = [new TextResourceContents { Uri = uri, MimeType = "application/json", Text = entry.ToJson() }]
        };
    }

    /// <summary>
    /// Builds a filtered, paginated execution log result for the get_execution_log tool.
    /// </summary>
    public CallToolResult BuildExecutionLogResult(string uri, string? level = null,
        string? category = null, string? search = null, int skip = 0, int take = 50)
    {
        if (!_toolLogStore.TryGet(uri, out var entry) || entry is null)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"Execution log not found for '{uri}'." }]
            };
        }

        var allEntries = entry.LogEntries;
        var filtered = FilterLogEntries(allEntries, level, category, search);
        var paged = filtered.Skip(skip).Take(take).ToList();

        var result = new
        {
            entry.ToolName,
            entry.ExitCode,
            entry.Summary,
            TotalEntries = allEntries.Count,
            FilteredCount = filtered.Count,
            Skip = skip,
            Take = take,
            LogEntries = paged
        };

        return new CallToolResult
        {
            Content = [new TextContentBlock
            {
                Text = JsonSerializer.Serialize(result, TxcOutputJsonOptions.Default)
            }]
        };
    }

    private static readonly string[] LogLevelOrder =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    private static IReadOnlyList<RedactedLogEntry> FilterLogEntries(
        IReadOnlyList<RedactedLogEntry> entries, string? level, string? category, string? search)
    {
        IEnumerable<RedactedLogEntry> result = entries;

        if (!string.IsNullOrWhiteSpace(level))
        {
            int minIndex = Array.IndexOf(LogLevelOrder, level);
            if (minIndex >= 0)
            {
                result = result.Where(e =>
                {
                    int idx = Array.IndexOf(LogLevelOrder, e.Level);
                    return idx >= minIndex;
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            result = result.Where(e =>
                e.Category.Contains(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            result = result.Where(e =>
                e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.Data != null && e.Data.Values.Any(v =>
                    v?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)));
        }

        return result.ToList();
    }

    private static string BuildResourceDescription(ToolLogStore.LogEntry entry)
    {
        var singleLineSummary = entry.Summary
            .ReplaceLineEndings(" ")
            .Trim();

        if (singleLineSummary.Length > 160)
        {
            singleLineSummary = singleLineSummary[..157] + "...";
        }

        return $"{singleLineSummary} Use resources/read to retrieve detailed diagnostics.";
    }
}
