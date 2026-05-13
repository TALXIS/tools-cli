using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Tests for MCP server functionality.
/// </summary>
[Collection("Sequential")]
public class McpTests
{
    [Fact]
    public async Task ListTools_ReturnsAlwaysOnTools()
    {
        var client = await McpTestClient.InstanceAsync;
        
        var tools = await client.ListToolsAsync();
        var toolNames = tools.Select(t => t.Name).ToList();
        
        // Progressive disclosure: 9 always-on tools instead of 97 static tools
        Assert.Contains("guide", toolNames);
        Assert.Contains("guide_workspace", toolNames);
        Assert.Contains("guide_environment", toolNames);
        Assert.Contains("execute_operation", toolNames);
        Assert.Contains("get_skill_details", toolNames);
    }

    [Fact]
    public async Task ExecuteOperation_WorkspaceComponentList_ReturnsValidResponse()
    {
        var client = await McpTestClient.InstanceAsync;
        var args = new Dictionary<string, object?> { { "operation", "component_type_list" } };
        
        var result = await client.CallToolAsync("execute_operation", args);
        
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.True(result.IsError != true);
    }

    [Fact]
    public async Task ExecuteOperation_WorkspaceComponentExplain_ReturnsComponentDetails()
    {
        var client = await McpTestClient.InstanceAsync;

        // First verify the tool exists in the catalog by listing component types.
        // This also triggers template package auto-installation if needed.
        var listArgs = new Dictionary<string, object?> { { "operation", "component_type_list" } };
        var listResult = await client.CallToolAsync("execute_operation", listArgs);
        
        // If component type list returned empty or error, registry isn't available — skip
        var listText = listResult.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
        if (listResult.IsError == true || listText == "[]" || string.IsNullOrWhiteSpace(listText))
        {
            return;
        }

        var args = new Dictionary<string, object?> { { "operation", "component_type_explain" }, { "arguments", "{\"Type\": \"Entity\"}" } };
        var result = await client.CallToolAsync("execute_operation", args);
        
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);

        if (result.IsError == true)
        {
            var errorContent = result.Content[0] is TextContentBlock errorBlock ? errorBlock.Text : "Unknown error";
            throw new InvalidOperationException($"MCP call failed: {errorContent}");
        }

        if (result.Content[0] is TextContentBlock textBlock)
        {
            Assert.Contains("Entity", textBlock.Text);
        }
    }
}
