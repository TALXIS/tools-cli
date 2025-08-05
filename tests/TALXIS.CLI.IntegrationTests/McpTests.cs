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
    public async Task ListTools_ReturnsExpectedTools()
    {
        var client = await McpClient.InstanceAsync;
        
        var tools = await client.ListToolsAsync();
        var toolNames = tools.Select(t => t.Name).ToList();
        
        Assert.Contains("workspace_component_list", toolNames);
        Assert.Contains("workspace_component_explain", toolNames);
    }

    [Fact]
    public async Task WorkspaceComponentList_ReturnsValidResponse()
    {
        var client = await McpClient.InstanceAsync;
        
        var result = await client.CallToolAsync("workspace_component_list");
        
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.True(result.IsError != true);
    }

    [Fact]
    public async Task WorkspaceComponentExplain_ReturnsComponentDetails()
    {
        var client = await McpClient.InstanceAsync;
        var args = new Dictionary<string, object> { { "Name", "pp-entity" } };
        
        var result = await client.CallToolAsync("workspace_component_explain", args);
        
        Assert.NotNull(result.Content);
        Assert.NotEmpty(result.Content);
        Assert.True(result.IsError != true);

        if (result.Content[0] is TextContentBlock textBlock)
        {
            Assert.Contains("Name:", textBlock.Text);
            Assert.Contains("pp-entity", textBlock.Text);
        }
    }
}
