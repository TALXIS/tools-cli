using System.Text.Json;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.Abstractions;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Builds MCP <see cref="CallToolResult"/> responses from CLI subprocess outcomes.
/// Log query and resource operations are handled by <see cref="ExecutionLogService"/>.
/// </summary>
internal sealed class McpToolResultFactory
{
    private readonly ToolLogStore _toolLogStore;
    private readonly Func<string?> _sessionIdAccessor;

    /// <param name="toolLogStore">Execution log store for diagnostics resources.</param>
    /// <param name="sessionIdAccessor">Provides the current session ID. Injected to avoid
    /// static coupling to <c>TxcTelemetrySetup</c>.</param>
    public McpToolResultFactory(ToolLogStore toolLogStore, Func<string?> sessionIdAccessor)
    {
        _toolLogStore = toolLogStore;
        _sessionIdAccessor = sessionIdAccessor;
    }

    public CallToolResult Build(string toolName, CliSubprocessResult result)
    {
        // Capture operation ID while Activity is still active — by the time
        // BuildFailureResult runs the Activity scope may have ended.
        var operationId = TxcActivitySource.CurrentOperationId;

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
            operationId);

        if (result.ExitCode == 0)
        {
            string successText = result.Output;
            if (result.StructuredEntries.Count > 0)
            {
                successText = $"{result.Output.TrimEnd()}{Environment.NewLine}{Environment.NewLine}" +
                    $"Diagnostics URI: {diagnosticsUri}";
            }

            // Try to parse CLI output as JSON for structuredContent
            JsonElement? structuredData = TryParseJson(result.Output?.Trim());
            var callResult = new CallToolResult
            {
                Content = [new TextContentBlock { Text = successText }]
            };
            if (structuredData.HasValue)
            {
                callResult.StructuredContent = JsonSerializer.SerializeToElement(new
                {
                    status = "succeeded",
                    data = structuredData.Value
                }, TxcOutputJsonOptions.Default);
            }
            return callResult;
        }

        string summary = BuildFailureSummary(toolName, result.Output, result.LastErrors, result.ExitCode);
        return BuildFailureResult(toolName, summary, diagnosticsUri, result.ExitCode, operationId);
    }

    public CallToolResult BuildExceptionResult(string toolName, Exception exception)
    {
        // Capture operation ID while Activity is still active.
        var operationId = TxcActivitySource.CurrentOperationId;

        // Surface the innermost exception message — for wrapped exceptions
        // (e.g. HttpRequestException → SocketException), the root cause is
        // more actionable than the outer wrapper message.
        var root = ExceptionHelpers.GetInnermostException(exception);
        var rootMessage = root != exception && !string.Equals(root.Message, exception.Message, StringComparison.Ordinal)
            ? root.Message : exception.Message;
        string summary = string.IsNullOrWhiteSpace(rootMessage)
            ? $"Tool '{toolName}' failed before execution completed."
            : LogRedactionFilter.Redact(rootMessage);
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
            operationId);

        return BuildFailureResult(toolName, summary, diagnosticsUri, exitCode: -1, operationId);
    }

    private static JsonElement? TryParseJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private CallToolResult BuildFailureResult(string toolName, string summary,
        string diagnosticsUri, int exitCode = -1, string? operationId = null)
    {
        var support = new SupportContext
        {
            SessionId = _sessionIdAccessor() ?? "unknown",
            OperationId = operationId ?? "unknown",
        };
        var supportText = support.FormatAsText();
        var supportBlock = string.IsNullOrEmpty(supportText) ? "" : $"{Environment.NewLine}{supportText}";

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
            ],
            StructuredContent = JsonSerializer.SerializeToElement(new
            {
                status = "failed",
                error = new
                {
                    message = summary,
                    exitCode,
                },
                diagnosticsUri,
                support
            }, TxcOutputJsonOptions.Default)
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

}
