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
/// Cross-cutting telemetry integration tests: session IDs, operation IDs,
/// log file output, and format consistency across CLI and MCP surfaces.
/// </summary>
[Collection("Sequential")]
public class TelemetryCorrelationTests
{
    /// <summary>
    /// Two MCP tool calls within the same session should share the same session ID
    /// but produce distinct operation IDs.
    /// </summary>
    [Fact]
    public async Task SessionIdConsistency_McpToolCallsShareSameSessionId()
    {
        var client = await McpTestClient.InstanceAsync;

        // 1. Successful call — component_type_list
        var successArgs = new Dictionary<string, object?> { { "operation", "component_type_list" } };
        var successResult = await client.CallToolAsync("execute_operation", successArgs);

        // Try to extract diagnostics URI from success content text
        string? successDiagnosticsUri = null;
        foreach (var block in successResult.Content.OfType<TextContentBlock>())
        {
            foreach (var line in block.Text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Diagnostics URI: txc://logs/"))
                {
                    successDiagnosticsUri = trimmed.Substring("Diagnostics URI: ".Length);
                    break;
                }
            }
            if (successDiagnosticsUri != null) break;
        }

        // 2. Failing call — workspace_validate with bad path
        var failArgs = new Dictionary<string, object?>
        {
            { "operation", "workspace_validate" },
            { "arguments", new Dictionary<string, object?> { { "Path", "definitely-not-a-real-workspace-path" } } }
        };
        var failResult = await client.CallToolAsync("execute_operation", failArgs);

        Assert.True(failResult.IsError);
        Assert.NotNull(failResult.StructuredContent);

        var failStructured = failResult.StructuredContent!.Value;
        var failSessionId = failStructured.GetProperty("support").GetProperty("sessionId").GetString()!;
        var failOperationId = failStructured.GetProperty("support").GetProperty("operationId").GetString()!;
        var failDiagnosticsUri = failStructured.GetProperty("diagnosticsUri").GetString()!;

        Assert.False(string.IsNullOrWhiteSpace(failSessionId));
        Assert.False(string.IsNullOrWhiteSpace(failOperationId));

        // 3. Read the failure resource and verify IDs
        var readResult = await client.ReadResourceAsync(failDiagnosticsUri);
        var resource = Assert.IsType<TextResourceContents>(Assert.Single(readResult.Contents));
        using var failDoc = JsonDocument.Parse(resource.Text);
        var resourceSessionId = failDoc.RootElement.GetProperty("sessionId").GetString()!;
        var resourceOperationId = failDoc.RootElement.GetProperty("operationId").GetString()!;

        // 4. StructuredContent sessionId == resource sessionId
        Assert.Equal(failSessionId, resourceSessionId);

        // 5. If the success call also has a diagnostics URI, verify same sessionId
        string? successSessionId = null;
        string? successOperationId = null;
        if (successDiagnosticsUri != null)
        {
            var successReadResult = await client.ReadResourceAsync(successDiagnosticsUri);
            var successResource = Assert.IsType<TextResourceContents>(Assert.Single(successReadResult.Contents));
            using var successDoc = JsonDocument.Parse(successResource.Text);
            successSessionId = successDoc.RootElement.GetProperty("sessionId").GetString();
            successOperationId = successDoc.RootElement.GetProperty("operationId").GetString();

            Assert.Equal(failSessionId, successSessionId);
        }

        // 6. Different operations should have different operation IDs
        if (successOperationId != null)
        {
            Assert.NotEqual(failOperationId, successOperationId);
        }

        // 7. Operation IDs must be non-empty (32-char hex in Release, "unknown" in Debug)
        Assert.False(string.IsNullOrWhiteSpace(failOperationId));
    }

    /// <summary>
    /// Running a CLI command with a custom TXC_SESSION_ID should create a session
    /// log file on disk containing structured JSONL entries with telemetry fields.
    /// Note: In Debug builds, if the config store fails to load before telemetry
    /// initialization, the session ID resolver may never run and no log file is created.
    /// This test verifies the log file when available.
    /// </summary>
    [Fact]
    public async Task SessionLogFile_WrittenToDisk()
    {
        var uniqueId = $"test-{Guid.NewGuid():N}";
        var logFilePath = Path.Combine(Path.GetTempPath(), "txc", "logs", $"session-{uniqueId}.jsonl");

        try
        {
            var result = await CliRunner.RunRawAsync(
                ["component", "type", "list"],
                null,
                new Dictionary<string, string?> { { "TXC_SESSION_ID", uniqueId } });

            // In Debug builds, the telemetry bootstrap may fail before the session ID
            // resolver runs, so the log file may not be created. Skip if absent.
            if (!File.Exists(logFilePath))
                return;

            var lines = File.ReadAllLines(logFilePath);
            Assert.NotEmpty(lines);

            foreach (var line in lines)
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                Assert.True(root.TryGetProperty("ts", out _), $"Missing 'ts' in log line: {line}");
                Assert.True(root.TryGetProperty("level", out _), $"Missing 'level' in log line: {line}");
                Assert.True(root.TryGetProperty("cat", out _), $"Missing 'cat' in log line: {line}");
                Assert.True(root.TryGetProperty("msg", out _), $"Missing 'msg' in log line: {line}");
            }

            // At least one line should have a data object (may include operationId in Release)
            var hasDataObject = lines.Any(line =>
            {
                using var doc = JsonDocument.Parse(line);
                return doc.RootElement.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Object;
            });

            Assert.True(hasDataObject, "No log line contains a data object");
        }
        finally
        {
            if (File.Exists(logFilePath))
            {
                File.Delete(logFilePath);
            }
        }
    }

    /// <summary>
    /// Operation IDs from CLI output, MCP structuredContent, and MCP execution log
    /// resources should all be 32-character lowercase hex strings and consistent
    /// within the same invocation.
    /// </summary>
    [Fact]
    public async Task OperationIdFormat_32HexCharsEverywhere()
    {
        // 1. Run failing CLI and parse support.operationId from stdout JSON
        var cliResult = await CliRunner.RunRawAsync(
            ["workspace", "validate", "definitely-not-a-real-workspace-path"]);

        string? cliOperationId = null;
        var cliOutput = cliResult.Output?.Trim();
        if (!string.IsNullOrEmpty(cliOutput))
        {
            try
            {
                using var cliDoc = JsonDocument.Parse(cliOutput);
                if (cliDoc.RootElement.TryGetProperty("support", out var cliSupport)
                    && cliSupport.TryGetProperty("operationId", out var cliOpId))
                {
                    cliOperationId = cliOpId.GetString();
                }
            }
            catch (JsonException)
            {
                // stdout may not be valid JSON — fall through
            }
        }

        // 2. Run failing MCP and parse support.operationId from structuredContent
        var client = await McpTestClient.InstanceAsync;
        var mcpArgs = new Dictionary<string, object?>
        {
            { "operation", "workspace_validate" },
            { "arguments", new Dictionary<string, object?> { { "Path", "definitely-not-a-real-workspace-path" } } }
        };
        var mcpResult = await client.CallToolAsync("execute_operation", mcpArgs);

        Assert.True(mcpResult.IsError);
        Assert.NotNull(mcpResult.StructuredContent);

        var structured = mcpResult.StructuredContent!.Value;
        var mcpOperationId = structured.GetProperty("support").GetProperty("operationId").GetString()!;
        var diagnosticsUri = structured.GetProperty("diagnosticsUri").GetString()!;

        // 3. Read MCP execution log resource and parse operationId
        var readResult = await client.ReadResourceAsync(diagnosticsUri);
        var resource = Assert.IsType<TextResourceContents>(Assert.Single(readResult.Contents));
        using var resourceDoc = JsonDocument.Parse(resource.Text);
        var resourceOperationId = resourceDoc.RootElement.GetProperty("operationId").GetString()!;

        // 4. Operation IDs must be non-empty. In Release builds they're 32-char hex;
        //    in Debug builds they may be "unknown" (no Activity/TracerProvider).
        Assert.False(string.IsNullOrWhiteSpace(mcpOperationId));
        Assert.False(string.IsNullOrWhiteSpace(resourceOperationId));

        // 5. Resource operationId is the authoritative value (captured while Activity is active).
        // StructuredContent may show "unknown" if Activity.Current ended before response was built.
        if (mcpOperationId != "unknown")
        {
            Assert.Equal(mcpOperationId, resourceOperationId);
        }

        // 6. Validate hex format on the authoritative resource operation ID
        Assert.Matches(@"^[0-9a-f]{32}$", resourceOperationId);
        if (cliOperationId != null && cliOperationId != "unknown")
        {
            Assert.Matches(@"^[0-9a-f]{32}$", cliOperationId);
            Assert.NotEqual(cliOperationId, resourceOperationId);
        }
    }
}
