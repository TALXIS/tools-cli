using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Tests that CLI and MCP produce equivalent results.
/// </summary>
[Collection("Sequential")]
public class EquivalenceTests
{
    [Theory]
    [MemberData(nameof(GetTestCases))]
    public async Task CliAndMcp_ProduceEquivalentOutput(string cliCommand, string mcpTool, Dictionary<string, object> mcpArgs)
    {
        var cliOutput = await CliRunner.RunAsync(cliCommand);
        
        var mcpClient = await McpClient.InstanceAsync;
        var mcpResult = await mcpClient.CallToolAsync(mcpTool, mcpArgs);
        
        var mcpOutput = ExtractTextContent(mcpResult);
        
        Assert.Equal(NormalizeOutput(cliOutput), NormalizeOutput(mcpOutput));
    }

    public static IEnumerable<object[]> GetTestCases()
    {
        yield return new object[] 
        { 
            "workspace component type list", 
            "workspace_component_type_list", 
            new Dictionary<string, object>() 
        };
        
        yield return new object[] 
        { 
            "workspace component type explain pp-entity", 
            "workspace_component_type_explain", 
            new Dictionary<string, object> { { "Type", "pp-entity" } } 
        };
    }

    private static string ExtractTextContent(CallToolResult result)
    {
        if (result.Content?.Count > 0 && result.Content[0] is TextContentBlock textBlock)
        {
            return textBlock.Text ?? "";
        }
        
        return result.Content?.Count > 0 ? JsonSerializer.Serialize(result.Content[0]) : "";
    }

    private static string NormalizeOutput(string output)
    {
        return string.Join("\n", output.Split('\n', '\r')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }
}
