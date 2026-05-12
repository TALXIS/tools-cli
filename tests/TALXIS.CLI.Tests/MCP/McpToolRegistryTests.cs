#pragma warning disable MCPEXP001

using ModelContextProtocol.Protocol;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class McpToolRegistryTests
{
    private readonly McpToolRegistry _registry = new();

    /// <summary>
    /// Helper to build Tool definitions from the catalog (replaces removed ListTools()).
    /// </summary>
    private List<Tool> GetAllTools() =>
        _registry.Catalog.GetAllEntries().Select(McpToolRegistry.BuildToolDefinition).ToList();

    [Fact]
    public void ListTools_ReturnsNonEmptyList()
    {
        var tools = GetAllTools();
        Assert.NotEmpty(tools);
    }

    [Fact]
    public void ListTools_ContainsCopilotInstructions()
    {
        var tools = GetAllTools();
        Assert.Contains(tools, t => t.Name == "copilot-instructions");
    }

    [Fact]
    public void ListTools_ContainsWorkspaceTools()
    {
        var tools = GetAllTools();
        var names = tools.Select(t => t.Name).ToList();

        Assert.Contains("workspace_component_parameter_list", names);
        Assert.Contains("workspace_component_create", names);
        Assert.Contains("workspace_explain", names);
    }

    [Fact]
    public void ListTools_ContainsComponentTypeTools()
    {
        var tools = GetAllTools();
        var names = tools.Select(t => t.Name).ToList();

        Assert.Contains("component_type_list", names);
        Assert.Contains("component_type_explain", names);
    }

    [Fact]
    public void ListTools_ContainsDataTools()
    {
        var tools = GetAllTools();
        var names = tools.Select(t => t.Name).ToList();

        Assert.Contains("data_model_convert", names);
        Assert.Contains("data_package_import", names);
    }

    [Fact]
    public void ListTools_AllHaveDescription()
    {
        var tools = GetAllTools();
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"Tool '{tool.Name}' has no description");
        }
    }

    [Fact]
    public void ListTools_AllHaveInputSchema()
    {
        var tools = GetAllTools();
        foreach (var tool in tools)
        {
            Assert.NotEqual(default, tool.InputSchema);
        }
    }

    [Fact]
    public void FindCommandType_ValidTool_ReturnsType()
    {
        var type = _registry.FindCommandTypeByToolName("component_type_list");
        Assert.NotNull(type);
    }

    [Fact]
    public void FindCommandType_CopilotInstructions_ReturnsType()
    {
        var type = _registry.FindCommandTypeByToolName("copilot-instructions");
        Assert.NotNull(type);
        Assert.Equal(typeof(CopilotInstructionsCliCommand), type);
    }

    [Fact]
    public void FindCommandType_InvalidTool_ReturnsNull()
    {
        var type = _registry.FindCommandTypeByToolName("nonexistent_tool_xyz");
        Assert.Null(type);
    }

    [Fact]
    public void ListTools_NoDuplicateNames()
    {
        var tools = GetAllTools();
        var names = tools.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void ListTools_LongRunningToolsAdvertiseTaskExecution()
    {
        var tools = GetAllTools();

        var deployTool = tools.First(t => t.Name == "environment_package_import");
        Assert.NotNull(deployTool.Execution);
        Assert.NotNull(deployTool.Execution.TaskSupport);

        var importTool = tools.First(t => t.Name == "data_package_import");
        Assert.NotNull(importTool.Execution);
        Assert.NotNull(importTool.Execution.TaskSupport);
    }

    [Fact]
    public void ListTools_QuickToolsDoNotAdvertiseTaskExecution()
    {
        var tools = GetAllTools();

        // workspace tools are quick operations, should not have task support
        var explainTool = tools.First(t => t.Name == "workspace_explain");
        Assert.Null(explainTool.Execution);

        // copilot-instructions runs in-process, should not have task support
        var copilotTool = tools.First(t => t.Name == "copilot-instructions");
        Assert.Null(copilotTool.Execution);
    }

    [Fact]
    public void GetDescriptor_ReturnsTaskSupportMetadata()
    {
        var deployDescriptor = _registry.GetDescriptor("environment_package_import");
        Assert.NotNull(deployDescriptor);
        Assert.True(deployDescriptor.SupportsTaskExecution);

        var explainDescriptor = _registry.GetDescriptor("workspace_explain");
        Assert.NotNull(explainDescriptor);
        Assert.False(explainDescriptor.SupportsTaskExecution);
    }

    [Fact]
    public void Catalog_IsPopulatedAtStartup()
    {
        Assert.True(_registry.Catalog.Count > 0);
    }

    [Fact]
    public void Catalog_EntriesHavePreBuiltSchemas()
    {
        foreach (var entry in _registry.Catalog.GetAllEntries())
        {
            Assert.NotEqual(default, entry.InputSchema);
        }
    }

    [Fact]
    public void Catalog_EntriesHaveWorkflowTags()
    {
        foreach (var entry in _registry.Catalog.GetAllEntries())
        {
            Assert.False(string.IsNullOrEmpty(entry.Workflow), $"Tool '{entry.Descriptor.Name}' has no workflow tag");
        }
    }

    [Fact]
    public void Catalog_GetCatalogPrompt_ContainsToolNames()
    {
        var prompt = _registry.Catalog.GetCatalogPrompt();
        Assert.Contains("workspace_explain", prompt);
        Assert.Contains("copilot-instructions", prompt);
    }
}
