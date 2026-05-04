using System.Text.Json;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class McpToolResultFactoryTests
{
    [Fact]
    public void Build_FailedResultWithSummary_AttachesReadableDiagnosticsResource()
    {
        var store = new ToolLogStore();
        var factory = new McpToolResultFactory(store);
        var result = new CliSubprocessResult(
            exitCode: 1,
            output: "{\"status\":\"failed\",\"message\":\"Validation complete: 3 error(s), 7 warning(s)\"}",
            lastErrors: "file1.xml(1,1): schema error",
            fullLog: "2026-05-04T10:00:00Z [Error] [WorkspaceValidateCliCommand] file1.xml(1,1): schema error");

        var toolResult = factory.Build("workspace_validate", result);

        Assert.True(toolResult.IsError);
        var text = Assert.IsType<TextContentBlock>(toolResult.Content[0]);
        Assert.Contains("Validation complete", text.Text);

        var link = Assert.IsType<ResourceLinkBlock>(toolResult.Content[1]);
        Assert.Equal("application/json", link.MimeType);

        var resource = factory.ReadResource(link.Uri);
        var contents = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));
        Assert.Equal("application/json", contents.MimeType);

        using var document = JsonDocument.Parse(contents.Text);
        Assert.Equal("tool-failure-details", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("workspace_validate", document.RootElement.GetProperty("toolName").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Contains("Validation complete", document.RootElement.GetProperty("summary").GetString());
        Assert.Contains("schema error", document.RootElement.GetProperty("fullLog").GetString());
    }

    [Fact]
    public void BuildExceptionResult_AttachesDiagnosticsResource()
    {
        var store = new ToolLogStore();
        var factory = new McpToolResultFactory(store);

        var toolResult = factory.BuildExceptionResult("workspace_validate", new InvalidOperationException("Boom"));

        Assert.True(toolResult.IsError);
        var text = Assert.IsType<TextContentBlock>(toolResult.Content[0]);
        Assert.Equal("Boom", text.Text);

        var link = Assert.IsType<ResourceLinkBlock>(toolResult.Content[1]);
        var resource = factory.ReadResource(link.Uri);
        var contents = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));

        using var document = JsonDocument.Parse(contents.Text);
        Assert.Equal(-1, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("Boom", document.RootElement.GetProperty("summary").GetString());
    }
}
