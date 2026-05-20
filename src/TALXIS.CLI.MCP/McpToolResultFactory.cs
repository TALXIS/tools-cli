using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Builds MCP tool results and exposes execution logs as fetchable resources.
/// </summary>
internal sealed class McpToolResultFactory
{
    private readonly ToolLogStore _toolLogStore;

    public McpToolResultFactory(ToolLogStore toolLogStore)
    {
        _toolLogStore = toolLogStore;
    }

    public CallToolResult Build(string toolName, CliSubprocessResult result)
    {
        // Store execution log for every run — clients may need to troubleshoot
        // unexpected output even from successful executions.
        // Use the root trace id as execution identity so diagnostics URI, telemetry,
        // and App Insights operation_Id all share one canonical id.
        string diagnosticsUri = _toolLogStore.Store(
            toolName,
            result.ExitCode,
            result.Output?.Trim(),
            result.LastErrors,
            result.StructuredEntries,
            Activity.Current?.TraceId.ToHexString());

        if (result.ExitCode == 0)
        {
            string successText = result.Output;
            if (result.StructuredEntries.Count > 0)
            {
                successText = $"{result.Output.TrimEnd()}{Environment.NewLine}{Environment.NewLine}" +
                    $"Diagnostics URI: {diagnosticsUri}";
            }

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = successText }]
            };
        }

        string summary = BuildFailureSummary(toolName, result.Output, result.LastErrors, result.ExitCode);
        return BuildFailureResult(toolName, summary, diagnosticsUri);
    }

    public CallToolResult BuildExceptionResult(string toolName, Exception exception)
    {
        string summary = string.IsNullOrWhiteSpace(exception.Message)
            ? $"Tool '{toolName}' failed before execution completed."
            : LogRedactionFilter.Redact(exception.Message);
        var exceptionEntry = new RedactedLogEntry(
            Timestamp: DateTime.UtcNow.ToString("o"),
            Level: "Critical",
            Category: toolName,
            Message: LogRedactionFilter.Redact(exception.ToString()));
        string diagnosticsUri = _toolLogStore.Store(
            toolName,
            -1,
            summary,
            summary,
            [exceptionEntry],
            Activity.Current?.TraceId.ToHexString());

        return BuildFailureResult(toolName, summary, diagnosticsUri);
    }

    public List<Resource> BuildResources()
    {
        return _toolLogStore.ListAll().Select(e => new Resource
        {
            Uri = e.Uri,
            Name = $"Execution log: {e.Entry.ToolName}",
            Description = BuildResourceDescription(e.Entry),
            MimeType = "application/json"
        }).ToList();
    }

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

    private static CallToolResult BuildFailureResult(string toolName, string summary, string diagnosticsUri)
    {
        var supportInfo = TALXIS.CLI.Logging.TxcSupportInfo.FormatEscalation();
        var supportBlock = string.IsNullOrEmpty(supportInfo) ? "" : $"{Environment.NewLine}{supportInfo}";

        string cliFriendlySummary =
            $"{summary}{Environment.NewLine}{supportBlock}{Environment.NewLine}" +
            $"Full execution log available via get_execution_log with uri=\"{diagnosticsUri}\".";

        return new CallToolResult
        {
            IsError = true,
            Content =
            [
                new TextContentBlock { Text = cliFriendlySummary },
                new ResourceLinkBlock
                {
                    Uri = diagnosticsUri,
                    Name = $"Execution log for {toolName}",
                    Description = "Fetch detailed execution log for this tool call via resources/read.",
                    MimeType = "application/json"
                }
            ]
        };
    }

    private static string BuildFailureSummary(string toolName, string output, string lastErrors, int exitCode)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output.Trim();
        }

        var firstErrorLine = lastErrors
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return !string.IsNullOrWhiteSpace(firstErrorLine)
            ? firstErrorLine
            : $"Tool '{toolName}' failed with exit code {exitCode}.";
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
