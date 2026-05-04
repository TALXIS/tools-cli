using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Builds MCP tool results and exposes failed tool diagnostics as fetchable resources.
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
        if (result.ExitCode == 0)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result.Output }]
            };
        }

        string summary = BuildFailureSummary(toolName, result.Output, result.LastErrors, result.ExitCode);
        string diagnosticsUri = _toolLogStore.StoreFailure(
            toolName,
            result.ExitCode,
            summary,
            result.LastErrors,
            result.FullLog);

        return BuildFailureResult(toolName, summary, diagnosticsUri);
    }

    public CallToolResult BuildExceptionResult(string toolName, Exception exception)
    {
        string summary = string.IsNullOrWhiteSpace(exception.Message)
            ? $"Tool '{toolName}' failed before execution completed."
            : LogRedactionFilter.Redact(exception.Message);
        string diagnosticsUri = _toolLogStore.StoreFailure(
            toolName,
            -1,
            summary,
            summary,
            LogRedactionFilter.Redact(exception.ToString()));

        return BuildFailureResult(toolName, summary, diagnosticsUri);
    }

    public List<Resource> BuildResources()
    {
        return _toolLogStore.ListAll().Select(e => new Resource
        {
            Uri = e.Uri,
            Name = $"Failure details: {e.Entry.ToolName}",
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

    private static CallToolResult BuildFailureResult(string toolName, string summary, string diagnosticsUri)
    {
        return new CallToolResult
        {
            IsError = true,
            Content =
            [
                new TextContentBlock { Text = summary },
                new ResourceLinkBlock
                {
                    Uri = diagnosticsUri,
                    Name = $"Failure details for {toolName}",
                    Description = "Fetch structured diagnostics for this failed tool call via resources/read.",
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
