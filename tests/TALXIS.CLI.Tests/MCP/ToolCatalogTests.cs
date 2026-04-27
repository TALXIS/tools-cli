#pragma warning disable MCPEXP001

using System.Text.Json;
using ModelContextProtocol.Protocol;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class ToolCatalogTests
{
    private readonly ToolCatalog _catalog = new();

    /// <summary>
    /// Creates a test McpToolDescriptor with the given name, description, and optional annotations.
    /// </summary>
    private static McpToolDescriptor CreateDescriptor(string name, string description = "Test tool", ToolAnnotations? annotations = null)
    {
        return new McpToolDescriptor
        {
            Name = name,
            Description = description,
            CliCommandClass = typeof(object), // placeholder for tests
            Annotations = annotations
        };
    }

    /// <summary>
    /// Builds a minimal valid JSON Schema element for test registration.
    /// </summary>
    private static JsonElement CreateTestSchema()
    {
        var json = """{"type":"object","properties":{"Param":{"type":"string"}}}""";
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Registers a tool in the catalog with a test schema and returns the descriptor.
    /// </summary>
    private McpToolDescriptor RegisterTool(string name, string description = "Test tool", ToolAnnotations? annotations = null)
    {
        var descriptor = CreateDescriptor(name, description, annotations);
        _catalog.Register(descriptor, CreateTestSchema());
        return descriptor;
    }

    [Fact]
    public void Register_And_GetEntry_ReturnsRegisteredEntry()
    {
        RegisterTool("workspace_test", "A workspace test tool");

        var entry = _catalog.GetEntry("workspace_test");

        Assert.NotNull(entry);
        Assert.Equal("workspace_test", entry.Descriptor.Name);
        Assert.Equal("A workspace test tool", entry.Descriptor.Description);
    }

    [Fact]
    public void GetAllEntries_ReturnsRegisteredEntries()
    {
        RegisterTool("tool_a", "Tool A");
        RegisterTool("tool_b", "Tool B");
        RegisterTool("tool_c", "Tool C");

        var entries = _catalog.GetAllEntries().ToList();

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Descriptor.Name == "tool_a");
        Assert.Contains(entries, e => e.Descriptor.Name == "tool_b");
        Assert.Contains(entries, e => e.Descriptor.Name == "tool_c");
    }

    [Fact]
    public void GetEntriesByWorkflow_FiltersCorrectly()
    {
        RegisterTool("workspace_alpha");
        RegisterTool("workspace_beta");
        RegisterTool("config_gamma");

        var localDev = _catalog.GetEntriesByWorkflow("local-development").ToList();
        var config = _catalog.GetEntriesByWorkflow("configuration").ToList();

        Assert.Equal(2, localDev.Count);
        Assert.All(localDev, e => Assert.Equal("local-development", e.Workflow));
        Assert.Single(config);
        Assert.Equal("configuration", config[0].Workflow);
    }

    [Fact]
    public void GetToolDetails_ReturnsOnlyMatchingEntries_SkipsUnknown()
    {
        RegisterTool("workspace_one");
        RegisterTool("workspace_two");

        var details = _catalog.GetToolDetails(["workspace_one", "nonexistent_tool", "workspace_two"]).ToList();

        Assert.Equal(2, details.Count);
        Assert.Contains(details, e => e.Descriptor.Name == "workspace_one");
        Assert.Contains(details, e => e.Descriptor.Name == "workspace_two");
    }

    [Fact]
    public void GetCatalogPrompt_IsNonEmpty_ContainsToolNames()
    {
        RegisterTool("workspace_create", "Create a workspace component");
        RegisterTool("config_show", "Show configuration");

        var prompt = _catalog.GetCatalogPrompt();

        Assert.False(string.IsNullOrWhiteSpace(prompt));
        Assert.Contains("workspace_create", prompt);
        Assert.Contains("config_show", prompt);
    }

    [Fact]
    public void GetWorkflowCatalogPrompt_ScopesToWorkflow()
    {
        RegisterTool("workspace_build", "Build workspace");
        RegisterTool("config_list", "List configs");

        var prompt = _catalog.GetWorkflowCatalogPrompt("local-development");

        Assert.Contains("workspace_build", prompt);
        Assert.DoesNotContain("config_list", prompt);
    }

    [Fact]
    public void DeriveCategory_ExtractsFirstSegment()
    {
        RegisterTool("environment_solution_import", "Import solution");

        var entry = _catalog.GetEntry("environment_solution_import");

        Assert.NotNull(entry);
        Assert.Equal("environment", entry.Category);
    }

    [Fact]
    public void DeriveWorkflow_Workspace_MapsToLocalDevelopment()
    {
        RegisterTool("workspace_test");

        var entry = _catalog.GetEntry("workspace_test");

        Assert.NotNull(entry);
        Assert.Equal("local-development", entry.Workflow);
    }

    [Fact]
    public void DeriveWorkflow_Config_MapsToConfiguration()
    {
        RegisterTool("config_test");

        var entry = _catalog.GetEntry("config_test");

        Assert.NotNull(entry);
        Assert.Equal("configuration", entry.Workflow);
    }

    [Fact]
    public void DeriveWorkflow_EnvironmentData_MapsToDataOperations()
    {
        RegisterTool("environment_data_query");

        var entry = _catalog.GetEntry("environment_data_query");

        Assert.NotNull(entry);
        Assert.Equal("data-operations", entry.Workflow);
    }

    [Fact]
    public void DeriveWorkflow_EnvironmentEntity_WithReadOnly_MapsToEnvironmentInspection()
    {
        RegisterTool("environment_entity_list", "List entities",
            new ToolAnnotations { ReadOnlyHint = true });

        var entry = _catalog.GetEntry("environment_entity_list");

        Assert.NotNull(entry);
        Assert.Equal("environment-inspection", entry.Workflow);
    }

    [Fact]
    public void DeriveWorkflow_EnvironmentEntity_WithoutReadOnly_MapsToEnvironmentMutation()
    {
        RegisterTool("environment_entity_update", "Update entity");

        var entry = _catalog.GetEntry("environment_entity_update");

        Assert.NotNull(entry);
        Assert.Equal("environment-mutation", entry.Workflow);
    }

    [Fact]
    public void Count_ReflectsRegisteredEntries()
    {
        Assert.Equal(0, _catalog.Count);

        RegisterTool("tool_one");
        Assert.Equal(1, _catalog.Count);

        RegisterTool("tool_two");
        Assert.Equal(2, _catalog.Count);
    }
}
