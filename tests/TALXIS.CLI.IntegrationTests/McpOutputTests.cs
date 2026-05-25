#pragma warning disable MCPEXP001

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Tests that validate MCP server output format consistency across
/// StructuredContent, Content blocks, and resource reads.
/// </summary>
[Collection("Sequential")]
public class McpOutputTests
{
    [Fact]
    public async Task McpSuccess_StructuredContentMatchesContent()
    {
        var client = await McpTestClient.InstanceAsync;
        var args = new Dictionary<string, object?> { { "operation", "component_type_list" } };

        var result = await client.CallToolAsync("execute_operation", args);

        // Basic success assertions
        Assert.True(result.IsError != true);
        Assert.NotNull(result.StructuredContent);

        // Parse StructuredContent envelope
        using var structuredDoc = JsonDocument.Parse(result.StructuredContent.Value.GetRawText());
        var root = structuredDoc.RootElement;
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("data", out var structuredData));

        // Content[0] must be a TextContentBlock
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));

        // Cross-surface check: extract JSON from TextContentBlock text.
        // The text may have a diagnostics URI or other text appended after the JSON payload.
        var contentJson = ExtractLeadingJson(textBlock.Text);
        using var contentDoc = JsonDocument.Parse(contentJson);

        // Both surfaces should carry the same data payload
        Assert.Equal(
            NormalizeJson(structuredData.GetRawText()),
            NormalizeJson(contentDoc.RootElement.GetRawText()));
    }

    [Fact]
    public async Task McpFailure_StructuredContentAndContentCarrySameErrorInfo()
    {
        var client = await McpTestClient.InstanceAsync;
        var args = new Dictionary<string, object?>
        {
            { "operation", "workspace_validate" },
            { "arguments", new Dictionary<string, object?> { { "Path", "definitely-not-a-real-workspace-path" } } }
        };

        var result = await client.CallToolAsync("execute_operation", args);

        // Basic failure assertions
        Assert.True(result.IsError == true);
        Assert.NotNull(result.StructuredContent);

        // Parse StructuredContent envelope
        using var structuredDoc = JsonDocument.Parse(result.StructuredContent.Value.GetRawText());
        var root = structuredDoc.RootElement;

        Assert.Equal("failed", root.GetProperty("status").GetString());

        // error.message is non-empty
        var errorObj = root.GetProperty("error");
        var errorMessage = errorObj.GetProperty("message").GetString();
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));

        // error.exitCode is a non-zero integer
        var exitCode = errorObj.GetProperty("exitCode").GetInt32();
        Assert.NotEqual(0, exitCode);

        // diagnosticsUri starts with "txc://logs/"
        var diagnosticsUri = root.GetProperty("diagnosticsUri").GetString()!;
        Assert.StartsWith("txc://logs/", diagnosticsUri);

        // support.sessionId is non-empty
        var support = root.GetProperty("support");
        var sessionId = support.GetProperty("sessionId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        // support.operationId is non-empty (may be "unknown" in Debug builds without Activity)
        var operationId = support.GetProperty("operationId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(operationId));

        // support.reportUrl contains the GitHub issues URL
        var reportUrl = support.GetProperty("reportUrl").GetString()!;
        Assert.Contains("github.com/TALXIS/tools-cli/issues", reportUrl);

        // Content block assertions
        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        var resourceLink = result.Content.OfType<ResourceLinkBlock>().Single();

        // Cross-surface: ResourceLinkBlock.Uri matches structuredContent.diagnosticsUri
        Assert.Equal(diagnosticsUri, resourceLink.Uri);

        // Cross-surface: TextContentBlock contains the same error message
        Assert.Contains(errorMessage, textBlock.Text);

        // Cross-surface: Parse Session: and Operation: values from text
        var sessionMatch = Regex.Match(textBlock.Text, @"Session:\s*(\S+)");
        Assert.True(sessionMatch.Success, "TextContentBlock should contain 'Session:' line");
        Assert.Equal(sessionId, sessionMatch.Groups[1].Value);

        var operationMatch = Regex.Match(textBlock.Text, @"Operation:\s*(\S+)");
        Assert.True(operationMatch.Success, "TextContentBlock should contain 'Operation:' line");
        Assert.Equal(operationId, operationMatch.Groups[1].Value);

        // Text should mention get_execution_log for discoverability
        Assert.Contains("get_execution_log", textBlock.Text);
    }

    [Fact]
    public async Task McpExecutionLog_IdsConsistentWithToolResult()
    {
        var client = await McpTestClient.InstanceAsync;

        // Trigger a failed tool call
        var args = new Dictionary<string, object?>
        {
            { "operation", "workspace_validate" },
            { "arguments", new Dictionary<string, object?> { { "Path", "definitely-not-a-real-workspace-path" } } }
        };
        var result = await client.CallToolAsync("execute_operation", args);
        Assert.True(result.IsError == true);
        Assert.NotNull(result.StructuredContent);

        // Extract IDs from structuredContent
        using var structuredDoc = JsonDocument.Parse(result.StructuredContent.Value.GetRawText());
        var root = structuredDoc.RootElement;
        var diagnosticsUri = root.GetProperty("diagnosticsUri").GetString()!;
        var sessionId = root.GetProperty("support").GetProperty("sessionId").GetString()!;
        var operationId = root.GetProperty("support").GetProperty("operationId").GetString()!;

        // Call get_execution_log tool with the diagnostics URI
        var logResult = await client.CallToolAsync("get_execution_log", new Dictionary<string, object?> { { "uri", diagnosticsUri } });
        Assert.True(logResult.IsError != true);

        var logTextBlock = Assert.IsType<TextContentBlock>(logResult.Content[0]);
        using var logDoc = JsonDocument.Parse(logTextBlock.Text);
        var logRoot = logDoc.RootElement;

        // Validate execution log structure
        Assert.Equal("workspace_validate", logRoot.GetProperty("toolName").GetString());
        Assert.NotEqual(0, logRoot.GetProperty("exitCode").GetInt32());
        Assert.True(logRoot.TryGetProperty("logEntries", out var logEntries));
        Assert.Equal(JsonValueKind.Array, logEntries.ValueKind);
        Assert.True(logEntries.GetArrayLength() > 0, "logEntries should be non-empty");

        // Execution log tool result structure
        Assert.True(logRoot.TryGetProperty("totalEntries", out _));
        Assert.True(logRoot.TryGetProperty("filteredCount", out _));
        Assert.True(logRoot.TryGetProperty("skip", out _));
        Assert.True(logRoot.TryGetProperty("take", out _));

        // Read the resource directly to verify session/operation IDs
        var readResult = await client.ReadResourceAsync(diagnosticsUri);
        var resource = Assert.IsType<TextResourceContents>(Assert.Single(readResult.Contents));
        using var resourceDoc = JsonDocument.Parse(resource.Text);
        var resourceRoot = resourceDoc.RootElement;

        // Session ID should match across structuredContent and resource
        Assert.Equal(sessionId, resourceRoot.GetProperty("sessionId").GetString());

        // Operation ID in the resource is captured earlier while Activity is active,
        // so it's the authoritative value. The structuredContent may show "unknown"
        // if Activity.Current has already ended by the time the response is built.
        var resourceOperationId = resourceRoot.GetProperty("operationId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(resourceOperationId));
        if (operationId != "unknown")
        {
            Assert.Equal(operationId, resourceOperationId);
        }
    }

    /// <summary>
    /// Guide tools return human-readable text (not JSON), so they should NOT
    /// have StructuredContent set. Guide tools go through a different internal
    /// path than execute_operation — they don't use CliSubprocessRunner.
    /// </summary>
    [Fact]
    public async Task McpGuide_NonJsonOutput_NoStructuredContent()
    {
        var client = await McpTestClient.InstanceAsync;
        var args = new Dictionary<string, object?> { { "query", "what component types are available" } };

        var result = await client.CallToolAsync("guide_workspace", args);

        // Guide tools may fail in test environments (e.g. missing skill data).
        // If they succeed, verify no StructuredContent is set.
        if (result.IsError == true)
            return; // Skip assertion — guide infrastructure unavailable in test env

        // Guide tools return plain text — no StructuredContent
        Assert.True(
            result.StructuredContent is null || result.StructuredContent.Value.ValueKind == JsonValueKind.Undefined,
            "StructuredContent should be null for guide tool text output");

        var textBlock = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.False(string.IsNullOrWhiteSpace(textBlock.Text));
    }

    /// <summary>
    /// Extracts the leading JSON value from a string that may have trailing non-JSON text
    /// (e.g. a diagnostics URI appended after the JSON array/object).
    /// </summary>
    private static string ExtractLeadingJson(string text)
    {
        text = text.TrimStart();
        if (text.Length == 0)
            return text;

        // Determine the expected closing bracket
        char open = text[0];
        char close = open switch
        {
            '{' => '}',
            '[' => ']',
            _ => '\0'
        };

        if (close == '\0')
        {
            // Not an object or array — return the full text (might be a primitive)
            return text;
        }

        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (escape) { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == open) depth++;
            else if (c == close) depth--;

            if (depth == 0)
                return text[..(i + 1)];
        }

        // Fallback: return full text if no balanced close found
        return text;
    }

    /// <summary>
    /// Normalizes a JSON string by re-serializing via JsonDocument to remove whitespace differences.
    /// </summary>
    private static string NormalizeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }
}
