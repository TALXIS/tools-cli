using System.Text.Json;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class McpToolResultFactoryTests
{
    private static List<RedactedLogEntry> MakeEntries(params string[] messages) =>
        messages.Select((m, i) => new RedactedLogEntry(
            $"2026-05-04T10:00:{i:D2}Z", "Error", "TestCategory", m)).ToList();

    [Fact]
    public void Build_FailedResultWithSummary_AttachesReadableDiagnosticsResource()
    {
        var store = new ToolLogStore(() => "test-session");
        var factory = new McpToolResultFactory(store, () => "test-session");
        var entries = new List<RedactedLogEntry>
        {
            new("2026-05-04T10:00:00Z", "Error", "WorkspaceValidateCliCommand", "file1.xml(1,1): schema error")
        };
        var result = new CliSubprocessResult(
            exitCode: 1,
            output: "{\"status\":\"failed\",\"message\":\"Validation complete: 3 error(s), 7 warning(s)\"}",
            lastErrors: "file1.xml(1,1): schema error",
            structuredEntries: entries);

        var toolResult = factory.Build("workspace_validate", result);

        Assert.True(toolResult.IsError);
        var text = Assert.IsType<TextContentBlock>(toolResult.Content[0]);
        Assert.Contains("Validation complete", text.Text);
        Assert.Contains("get_execution_log", text.Text);

        var link = Assert.IsType<ResourceLinkBlock>(toolResult.Content[1]);
        Assert.Equal("application/json", link.MimeType);
        Assert.Contains(link.Uri, text.Text);

        var resource = factory.ReadResource(link.Uri);
        var contents = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));
        Assert.Equal("application/json", contents.MimeType);

        using var document = JsonDocument.Parse(contents.Text);
        Assert.Equal("tool-execution-log", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("workspace_validate", document.RootElement.GetProperty("toolName").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Contains("Validation complete", document.RootElement.GetProperty("summary").GetString());
        // LogEntries are stored structured — verify via the log entries array
        Assert.True(document.RootElement.GetProperty("logEntries").GetArrayLength() > 0);
    }

    [Fact]
    public void BuildExceptionResult_AttachesDiagnosticsResource()
    {
        var store = new ToolLogStore(() => "test-session");
        var factory = new McpToolResultFactory(store, () => "test-session");

        var toolResult = factory.BuildExceptionResult("workspace_validate", new InvalidOperationException("Boom"));

        Assert.True(toolResult.IsError);
        var text = Assert.IsType<TextContentBlock>(toolResult.Content[0]);
        Assert.Contains("Boom", text.Text);
        Assert.Contains("get_execution_log", text.Text);

        var link = Assert.IsType<ResourceLinkBlock>(toolResult.Content[1]);
        var resource = factory.ReadResource(link.Uri);
        var contents = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));

        using var document = JsonDocument.Parse(contents.Text);
        Assert.Equal(-1, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("Boom", document.RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public void BuildExecutionLogResult_ReturnsStructuredLogEntries()
    {
        var store = new ToolLogStore(() => "test-session");
        var factory = new McpToolResultFactory(store, () => "test-session");
        var entries = new List<RedactedLogEntry>
        {
            new("2026-05-04T10:00:00Z", "Error", "TestCategory", "schema error"),
            new("2026-05-04T10:00:01Z", "Warning", "TestCategory", "minor issue")
        };
        var uri = store.Store("workspace_validate", 1, "summary", "error", entries);

        var toolResult = factory.BuildExecutionLogResult(uri);

        Assert.True(toolResult.IsError != true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(toolResult.Content));

        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal("workspace_validate", document.RootElement.GetProperty("toolName").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("totalEntries").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("filteredCount").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("skip").GetInt32());
        Assert.Equal(50, document.RootElement.GetProperty("take").GetInt32());

        var logEntries = document.RootElement.GetProperty("logEntries");
        Assert.Equal(2, logEntries.GetArrayLength());
        Assert.Equal("schema error", logEntries[0].GetProperty("message").GetString());
        Assert.Equal("minor issue", logEntries[1].GetProperty("message").GetString());
    }

    [Fact]
    public void BuildExecutionLogResult_FiltersByLevel()
    {
        var store = new ToolLogStore(() => "test-session");
        var factory = new McpToolResultFactory(store, () => "test-session");
        var entries = new List<RedactedLogEntry>
        {
            new("2026-05-04T10:00:00Z", "Information", "Cat", "info msg"),
            new("2026-05-04T10:00:01Z", "Warning", "Cat", "warn msg"),
            new("2026-05-04T10:00:02Z", "Error", "Cat", "error msg")
        };
        var uri = store.Store("tool", 1, "summary", "error", entries);

        var toolResult = factory.BuildExecutionLogResult(uri, level: "Warning");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(toolResult.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(3, document.RootElement.GetProperty("totalEntries").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("filteredCount").GetInt32());
        var logEntries = document.RootElement.GetProperty("logEntries");
        Assert.Equal(2, logEntries.GetArrayLength());
        Assert.Equal("warn msg", logEntries[0].GetProperty("message").GetString());
        Assert.Equal("error msg", logEntries[1].GetProperty("message").GetString());
    }

    [Fact]
    public void BuildExecutionLogResult_SearchesMessages()
    {
        var store = new ToolLogStore(() => "test-session");
        var factory = new McpToolResultFactory(store, () => "test-session");
        var entries = new List<RedactedLogEntry>
        {
            new("2026-05-04T10:00:00Z", "Error", "Cat", "schema validation failed"),
            new("2026-05-04T10:00:01Z", "Error", "Cat", "connection timeout"),
            new("2026-05-04T10:00:02Z", "Warning", "Cat", "schema warning")
        };
        var uri = store.Store("tool", 1, "summary", "error", entries);

        var toolResult = factory.BuildExecutionLogResult(uri, search: "schema");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(toolResult.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(2, document.RootElement.GetProperty("filteredCount").GetInt32());
    }

    [Fact]
    public void BuildExecutionLogResult_SupportsPaging()
    {
        var store = new ToolLogStore(() => "test-session");
        var factory = new McpToolResultFactory(store, () => "test-session");
        var entries = Enumerable.Range(0, 10)
            .Select(i => new RedactedLogEntry($"2026-05-04T10:00:{i:D2}Z", "Error", "Cat", $"msg {i}"))
            .ToList();
        var uri = store.Store("tool", 1, "summary", "error", entries);

        var toolResult = factory.BuildExecutionLogResult(uri, skip: 3, take: 2);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(toolResult.Content));
        using var document = JsonDocument.Parse(text.Text);
        Assert.Equal(10, document.RootElement.GetProperty("totalEntries").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("skip").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("take").GetInt32());
        var logEntries = document.RootElement.GetProperty("logEntries");
        Assert.Equal(2, logEntries.GetArrayLength());
        Assert.Equal("msg 3", logEntries[0].GetProperty("message").GetString());
        Assert.Equal("msg 4", logEntries[1].GetProperty("message").GetString());
    }

    [Fact]
    public void Build_SuccessResult_StoresLogAndIncludesDiagnosticsUri()
    {
        var store = new ToolLogStore(() => "test-session");
        var factory = new McpToolResultFactory(store, () => "test-session");
        var entries = new List<RedactedLogEntry>
        {
            new("2026-05-04T10:00:00Z", "Information", "Cat", "all good")
        };
        var result = new CliSubprocessResult(
            exitCode: 0,
            output: "Success output",
            lastErrors: "",
            structuredEntries: entries);

        var toolResult = factory.Build("my_tool", result);

        Assert.True(toolResult.IsError != true);
        var text = Assert.IsType<TextContentBlock>(toolResult.Content[0]);
        Assert.Contains("Success output", text.Text);
        Assert.Contains("Diagnostics URI:", text.Text);

        // Verify the log was stored and is retrievable
        Assert.Equal(1, store.ListAll().Count);
    }
}
