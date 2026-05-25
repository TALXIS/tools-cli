using System.Text.Json;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.Abstractions;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.MCP;

/// <summary>
/// Single builder for all MCP <see cref="CallToolResult"/> responses.
/// Enforces the content/structuredContent contract: both surfaces always carry
/// the same information — <c>content</c> for humans, <c>structuredContent</c> for machines/AI.
/// <para>
/// All MCP tool handlers should use this factory instead of constructing
/// <see cref="CallToolResult"/> directly. See TXC029 analyzer.
/// </para>
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

    // ── CLI subprocess results (data commands, mutative commands) ──────────

    /// <summary>
    /// Builds a result from a CLI subprocess execution. Handles both success
    /// and failure, detecting <c>CommandResultEnvelope</c> JSON to produce
    /// clean human-readable <c>content</c> and typed <c>structuredContent</c>.
    /// </summary>
    public CallToolResult BuildDataResult(string toolName, CliSubprocessResult result)
    {
        // Capture operation ID while Activity is still active — by the time
        // BuildFailureResult runs the Activity scope may have ended.
        var operationId = TxcActivitySource.CurrentOperationId;

        // Store execution log for every run — clients may need to troubleshoot
        // unexpected output even from successful executions.
        string diagnosticsUri = _toolLogStore.Store(
            toolName,
            result.ExitCode,
            result.Output?.Trim(),
            result.LastErrors,
            result.StructuredEntries,
            operationId);

        // ── Success path ──
        if (result.ExitCode == 0)
        {
            // Detect CommandResultEnvelope on success (mutative commands like create/delete/update).
            // Extract the human-readable message instead of showing raw JSON in content.
            var successEnvelope = TryParseCliEnvelope(result.Output?.Trim());
            if (successEnvelope != null)
            {
                return BuildSuccessEnvelopeResult(successEnvelope, diagnosticsUri);
            }

            // Non-envelope output: data tables, JSON objects, plain text
            string successText = result.Output;
            if (result.StructuredEntries.Count > 0)
            {
                successText = $"{result.Output.TrimEnd()}{Environment.NewLine}{Environment.NewLine}" +
                    $"Diagnostics URI: {diagnosticsUri}";
            }

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

        // ── Failure path ──
        // When CLI outputs a CommandResultEnvelope JSON (status/message/exitCode/support),
        // extract the human-readable message and reuse the parsed fields so both MCP
        // surfaces carry the same information without double-nesting raw JSON.
        var envelope = TryParseCliEnvelope(result.Output?.Trim());
        if (envelope != null)
        {
            return BuildFailureResult(
                toolName,
                summary: envelope.Message ?? $"Tool '{toolName}' failed with exit code {result.ExitCode}.",
                diagnosticsUri,
                envelope.ExitCode ?? result.ExitCode,
                operationId,
                cliSupport: envelope.Support);
        }

        string summary = BuildFailureSummary(toolName, result.Output, result.LastErrors, result.ExitCode);
        return BuildFailureResult(toolName, summary, diagnosticsUri, result.ExitCode, operationId);
    }

    /// <summary>
    /// Builds a result from an exception that occurred before or during CLI execution.
    /// </summary>
    public CallToolResult BuildExceptionResult(string toolName, Exception exception)
    {
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

    // ── Text-only results (guides, skill content, prose) ──────────────────

    /// <summary>
    /// Builds a text-only result for prose responses (guide tools, skill content).
    /// No <c>structuredContent</c> is set — intentionally text-only since
    /// there is no structured data to represent.
    /// </summary>
    public static CallToolResult BuildTextResult(string text)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }]
        };
    }

    // ── Error results (parameter validation, not-found) ───────────────────

    /// <summary>
    /// Builds an error result for parameter validation, not-found, or other
    /// non-CLI errors. Both <c>content</c> and <c>structuredContent</c> carry
    /// the same error message.
    /// </summary>
    public static CallToolResult BuildErrorResult(string message)
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(new
            {
                status = "failed",
                error = new { message }
            }, TxcOutputJsonOptions.Default)
        };
    }

    // ── Execution log results ─────────────────────────────────────────────

    /// <summary>
    /// Builds a filtered, paginated execution log result.
    /// Both <c>content</c> (JSON text) and <c>structuredContent</c> carry the same data.
    /// </summary>
    public CallToolResult BuildExecutionLogResult(string uri, string? level = null,
        string? category = null, string? search = null, int skip = 0, int take = 50)
    {
        if (!_toolLogStore.TryGet(uri, out var entry) || entry is null)
        {
            return BuildErrorResult($"Execution log not found for '{uri}'.");
        }

        var allEntries = entry.LogEntries;
        var filtered = ExecutionLogService.FilterLogEntries(allEntries, level, category, search);
        var paged = filtered.Skip(skip).Take(take).ToList();

        var resultData = new
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
                Text = JsonSerializer.Serialize(resultData, TxcOutputJsonOptions.Default)
            }],
            StructuredContent = JsonSerializer.SerializeToElement(resultData, TxcOutputJsonOptions.Default)
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a success result from a parsed <c>CommandResultEnvelope</c>.
    /// Extracts the human-readable message for <c>content</c> and uses the
    /// envelope fields for <c>structuredContent</c> — no double-nesting.
    /// </summary>
    private static CallToolResult BuildSuccessEnvelopeResult(CliEnvelopeFields envelope, string diagnosticsUri)
    {
        var message = envelope.Message ?? "Operation completed successfully.";

        // Both surfaces carry the same information:
        // content = human-readable text with diagnostics URI appended
        // structuredContent = typed JSON with the same fields
        var contentText = $"{message}\n\nDiagnostics URI: {diagnosticsUri}";

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = contentText },
                new ResourceLinkBlock { Name = "Execution log", Uri = diagnosticsUri, Title = "Execution log", MimeType = "application/json" }
            ],
            StructuredContent = JsonSerializer.SerializeToElement(new
            {
                status = "succeeded",
                message,
                id = envelope.Id,
                diagnosticsUri
            }, TxcOutputJsonOptions.Default)
        };
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

    /// <summary>
    /// Tries to parse CLI stdout as a <c>CommandResultEnvelope</c> JSON
    /// (<c>{"status":"...","message":"...","exitCode":1,"support":{...}}</c>).
    /// Returns null if the output is not valid JSON or does not match the envelope shape.
    /// </summary>
    private static CliEnvelopeFields? TryParseCliEnvelope(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // Must have "status" and "message" to be recognized as an envelope
            if (!root.TryGetProperty("status", out var statusProp) || statusProp.ValueKind != JsonValueKind.String)
                return null;
            if (!root.TryGetProperty("message", out var messageProp) || messageProp.ValueKind != JsonValueKind.String)
                return null;

            int? exitCode = root.TryGetProperty("exitCode", out var ecProp) && ecProp.ValueKind == JsonValueKind.Number
                ? ecProp.GetInt32() : null;

            string? id = root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString() : null;

            SupportContext? support = null;
            if (root.TryGetProperty("support", out var supportProp) && supportProp.ValueKind == JsonValueKind.Object)
            {
                support = JsonSerializer.Deserialize<SupportContext>(supportProp.GetRawText(), TxcOutputJsonOptions.Default);
            }

            return new CliEnvelopeFields(messageProp.GetString(), exitCode, id, support);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CliEnvelopeFields(string? Message, int? ExitCode, string? Id, SupportContext? Support);

    private CallToolResult BuildFailureResult(string toolName, string summary,
        string diagnosticsUri, int exitCode = -1, string? operationId = null,
        SupportContext? cliSupport = null)
    {
        // Prefer support context from the CLI subprocess envelope (it carries the actual
        // operation ID from the child process). Fall back to MCP-level context.
        var sessionId = _sessionIdAccessor() ?? "unknown";
        var opId = operationId ?? "unknown";
        var support = cliSupport != null
            ? new SupportContext
            {
                SessionId = !string.IsNullOrEmpty(cliSupport.SessionId) ? cliSupport.SessionId : sessionId,
                OperationId = !string.IsNullOrEmpty(cliSupport.OperationId) ? cliSupport.OperationId : opId,
            }
            : new SupportContext
            {
                SessionId = sessionId,
                OperationId = opId,
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
