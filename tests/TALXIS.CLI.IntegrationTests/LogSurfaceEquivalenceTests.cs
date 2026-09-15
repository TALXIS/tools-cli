#pragma warning disable MCPEXP001

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Validates that all user-facing log surfaces tell the same story when a failure occurs.
/// Surface A: Console stderr (real-time terminal output)
/// Surface B: MCP execution log (get_execution_log tool)
/// Surface C: Session log file ($TMPDIR/txc/logs/session-{sessionId}.jsonl)
/// Surface D: App Insights (tested separately in smoke tests)
/// </summary>
[Collection("Sequential")]
public class LogSurfaceEquivalenceTests
{
    [Fact]
    public async Task AllLogSurfaces_SameFailure_SameData()
    {
        // ── Step 1: Trigger failure via MCP ──────────────────────────────
        var client = await McpTestClient.InstanceAsync;
        var args = new Dictionary<string, object?>
        {
            { "operation", "workspace_validate" },
            { "arguments", new Dictionary<string, object?> { { "Path", "definitely-not-a-real-workspace-path" } } }
        };

        var result = await client.CallToolAsync("execute_operation", args);

        Assert.True(result.IsError == true, "Expected MCP call to fail");
        Assert.NotNull(result.StructuredContent);

        using var structuredDoc = JsonDocument.Parse(result.StructuredContent.Value.GetRawText());
        var root = structuredDoc.RootElement;

        var operationId = root.GetProperty("support").GetProperty("operationId").GetString()!;
        var sessionId = root.GetProperty("support").GetProperty("sessionId").GetString()!;
        var errorMessage = root.GetProperty("error").GetProperty("message").GetString()!;
        var exitCodeMcp = root.GetProperty("error").GetProperty("exitCode").GetInt32();
        var diagnosticsUri = root.GetProperty("diagnosticsUri").GetString()!;

        Assert.False(string.IsNullOrWhiteSpace(errorMessage), "MCP error.message should be non-empty");
        Assert.NotEqual(0, exitCodeMcp);

        // ── Step 2: Collect Surface B — MCP execution log ────────────────
        var logResult = await client.CallToolAsync("get_execution_log", new Dictionary<string, object?> { { "uri", diagnosticsUri } });
        Assert.True(logResult.IsError != true, "get_execution_log should succeed");

        var logTextBlock = Assert.IsType<TextContentBlock>(logResult.Content[0]);
        using var logDoc = JsonDocument.Parse(logTextBlock.Text);
        var logRoot = logDoc.RootElement;

        var execLogToolName = logRoot.GetProperty("toolName").GetString()!;
        var execLogExitCode = logRoot.GetProperty("exitCode").GetInt32();
        var execLogSummary = logRoot.GetProperty("summary").GetString()!;
        var execLogEntries = logRoot.GetProperty("logEntries");
        Assert.Equal(JsonValueKind.Array, execLogEntries.ValueKind);
        var execLogEntryCount = execLogEntries.GetArrayLength();
        Assert.True(execLogEntryCount > 0, "Execution log should have at least one entry");

        var execLogItems = execLogEntries.EnumerateArray()
            .Select(e => new
            {
                Level = e.GetProperty("level").GetString()!,
                Message = e.GetProperty("message").GetString()!
            })
            .ToList();

        // Read the resource directly for session/operation IDs
        var readResult = await client.ReadResourceAsync(diagnosticsUri);
        var resource = Assert.IsType<TextResourceContents>(Assert.Single(readResult.Contents));
        using var resourceDoc = JsonDocument.Parse(resource.Text);
        var resourceRoot = resourceDoc.RootElement;

        var resourceSessionId = resourceRoot.GetProperty("sessionId").GetString()!;
        var resourceOperationId = resourceRoot.GetProperty("operationId").GetString()!;

        // ── Step 3: Collect Surface C — Session log file ─────────────────
        var sessionLogPath = Path.Combine(Path.GetTempPath(), "txc", "logs", $"session-{sessionId}.jsonl");
        Assert.True(File.Exists(sessionLogPath), $"Session log file should exist at {sessionLogPath}");

        var allLines = await File.ReadAllLinesAsync(sessionLogPath);
        Assert.True(allLines.Length > 0, "Session log file should not be empty");

        // Parse each JSONL line and filter by operationId when available.
        // In Debug builds, CLI subprocesses don't have Activity/TracerProvider, so
        // log entries won't have an operationId. Fall back to all entries in that case.
        var sessionLogEntries = new List<(string Level, string Message)>();
        var filteredByOpId = new List<(string Level, string Message)>();
        foreach (var line in allLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var lineDoc = JsonDocument.Parse(line);
            var lineRoot = lineDoc.RootElement;

            var level = lineRoot.GetProperty("level").GetString()!;
            var msg = lineRoot.GetProperty("msg").GetString()!;

            sessionLogEntries.Add((level, msg));

            if (lineRoot.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("operationId", out var opIdProp) &&
                opIdProp.GetString() == resourceOperationId)
            {
                filteredByOpId.Add((level, msg));
            }
        }

        // Use filtered entries if available, otherwise fall back to all entries
        var effectiveLogEntries = filteredByOpId.Count > 0 ? filteredByOpId : sessionLogEntries;
        Assert.True(effectiveLogEntries.Count > 0, "Session log file should have entries");

        // ── Step 4: Collect Surface A — CLI direct invocation ────────────
        // Use positional arg (workspace validate takes <path> positionally, not --path)
        var cliResult = await CliRunner.RunRawAsync(
            ["workspace", "validate", "definitely-not-a-real-workspace-path"]);

        Assert.NotEqual(0, cliResult.ExitCode);

        // ── Step 5: Cross-surface metadata assertions ────────────────────

        // — Error message consistency —
        Assert.Contains("not found", errorMessage, StringComparison.OrdinalIgnoreCase);

        Assert.True(execLogSummary.Contains(errorMessage) || errorMessage.Contains(execLogSummary),
            $"Execution log summary should overlap with MCP error. Summary: '{execLogSummary}', MCP: '{errorMessage}'");

        Assert.True(
            sessionLogEntries.Any(e =>
                e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)),
            "At least one session log file entry should contain 'not found'");

        Assert.Contains("not found", cliResult.Error, StringComparison.OrdinalIgnoreCase);

        // — Exit code consistency —
        Assert.Equal(exitCodeMcp, cliResult.ExitCode);
        Assert.Equal(exitCodeMcp, execLogExitCode);

        // — Session ID consistency (MCP surfaces) —
        Assert.Equal(sessionId, resourceSessionId);
        Assert.Contains(sessionId, sessionLogPath);

        // — Operation ID consistency (MCP surfaces) —
        // Resource operationId is authoritative (captured while Activity is active).
        // StructuredContent may show "unknown" if Activity scope ended before response was built.
        if (operationId != "unknown")
        {
            Assert.Equal(operationId, resourceOperationId);
        }
        // Use resource operationId as the canonical value for further comparisons
        var canonicalOperationId = operationId != "unknown" ? operationId : resourceOperationId;

        // — Log entries equivalence (Surface B ↔ Surface C) —
        foreach (var execEntry in execLogItems)
        {
            Assert.True(
                effectiveLogEntries.Any(fileEntry =>
                    fileEntry.Level == execEntry.Level &&
                    (fileEntry.Message.Contains(execEntry.Message) || execEntry.Message.Contains(fileEntry.Message))),
                $"Execution log entry (level={execEntry.Level}, message='{execEntry.Message}') " +
                "should have a corresponding entry in the session log file");
        }

        Assert.True(effectiveLogEntries.Count >= execLogItems.Count,
            $"Session log file entries ({effectiveLogEntries.Count}) should be >= execution log entries ({execLogItems.Count})");
    }
}
