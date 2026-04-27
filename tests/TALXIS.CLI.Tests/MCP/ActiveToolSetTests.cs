#pragma warning disable MCPEXP001

using ModelContextProtocol.Protocol;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class ActiveToolSetTests
{
    private readonly ActiveToolSet _toolSet = new();

    /// <summary>
    /// Creates a simple Tool with the given name and description.
    /// </summary>
    private static Tool CreateTool(string name, string description = "Test tool")
    {
        return new Tool { Name = name, Description = description };
    }

    [Fact]
    public void AddAlwaysOn_MakesToolVisibleInListActiveTools()
    {
        var tool = CreateTool("guide", "Guide tool");

        _toolSet.AddAlwaysOn(tool);

        var active = _toolSet.ListActiveTools();
        Assert.Contains(active, t => t.Name == "guide");
    }

    [Fact]
    public void InjectTools_AddsToolsToActiveSet()
    {
        var tools = new[] { CreateTool("workspace_build"), CreateTool("workspace_test") };

        _toolSet.InjectTools(tools);

        var active = _toolSet.ListActiveTools();
        Assert.Contains(active, t => t.Name == "workspace_build");
        Assert.Contains(active, t => t.Name == "workspace_test");
    }

    [Fact]
    public void InjectTools_ReinjectionUpdatesLruOrder()
    {
        // First injection
        var tools = new[] { CreateTool("workspace_build") };
        _toolSet.InjectTools(tools);
        Assert.Equal(1, _toolSet.InjectedCount);

        // Second injection of same tool — LRU order updated, count stays the same
        _toolSet.InjectTools(tools);
        Assert.Equal(1, _toolSet.InjectedCount);
        Assert.True(_toolSet.IsActive("workspace_build"));
    }

    [Fact]
    public void ListActiveTools_ReturnsAlwaysOnAndInjected()
    {
        _toolSet.AddAlwaysOn(CreateTool("guide", "Always-on guide"));
        _toolSet.InjectTools([CreateTool("workspace_build", "Injected build")]);

        var active = _toolSet.ListActiveTools();

        Assert.Equal(2, active.Count);
        Assert.Contains(active, t => t.Name == "guide");
        Assert.Contains(active, t => t.Name == "workspace_build");
    }

    [Fact]
    public void LruEviction_RemovesOldestWhenOverCap()
    {
        _toolSet.MaxInjectedTools = 3;

        // Inject 3 tools (fills to cap)
        _toolSet.InjectTools([CreateTool("tool_1"), CreateTool("tool_2"), CreateTool("tool_3")]);
        Assert.Equal(3, _toolSet.InjectedCount);

        // Inject a 4th — should evict the oldest (tool_1)
        _toolSet.InjectTools([CreateTool("tool_4")]);

        Assert.Equal(3, _toolSet.InjectedCount);
        Assert.False(_toolSet.IsActive("tool_1"), "tool_1 should have been evicted");
        Assert.True(_toolSet.IsActive("tool_2"));
        Assert.True(_toolSet.IsActive("tool_3"));
        Assert.True(_toolSet.IsActive("tool_4"));
    }

    [Fact]
    public void AlwaysOnTools_SurviveEviction()
    {
        _toolSet.MaxInjectedTools = 2;

        _toolSet.AddAlwaysOn(CreateTool("guide", "Always-on"));
        _toolSet.InjectTools([CreateTool("tool_1"), CreateTool("tool_2")]);

        // Inject a third tool — should evict tool_1, but guide stays
        _toolSet.InjectTools([CreateTool("tool_3")]);

        Assert.True(_toolSet.IsActive("guide"), "Always-on tool should survive eviction");
        Assert.False(_toolSet.IsActive("tool_1"), "tool_1 should have been evicted");
        Assert.True(_toolSet.IsActive("tool_2"));
        Assert.True(_toolSet.IsActive("tool_3"));
    }

    [Fact]
    public void IsActive_ReturnsTrueForAlwaysOnAndInjected()
    {
        _toolSet.AddAlwaysOn(CreateTool("guide"));
        _toolSet.InjectTools([CreateTool("workspace_build")]);

        Assert.True(_toolSet.IsActive("guide"));
        Assert.True(_toolSet.IsActive("workspace_build"));
    }

    [Fact]
    public void IsActive_ReturnsFalseForUnknown()
    {
        Assert.False(_toolSet.IsActive("nonexistent_tool"));
    }

    [Fact]
    public void InjectTools_DoesNotAddAlwaysOnTools()
    {
        _toolSet.AddAlwaysOn(CreateTool("guide", "Always-on guide"));

        // Try to inject the same tool that's already always-on
        _toolSet.InjectTools([CreateTool("guide", "Injected guide")]);

        // guide was skipped, no other tools injected
        Assert.Equal(1, _toolSet.AlwaysOnCount);
        Assert.Equal(0, _toolSet.InjectedCount);
    }

    [Fact]
    public void Count_ReflectsAlwaysOnAndInjected()
    {
        Assert.Equal(0, _toolSet.Count);

        _toolSet.AddAlwaysOn(CreateTool("guide"));
        Assert.Equal(1, _toolSet.Count);

        _toolSet.InjectTools([CreateTool("workspace_build"), CreateTool("workspace_test")]);
        Assert.Equal(3, _toolSet.Count);
    }
}
