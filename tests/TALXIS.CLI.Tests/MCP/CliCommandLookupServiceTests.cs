using DotMake.CommandLine;
using TALXIS.CLI.MCP;
using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class CliCommandLookupServiceTests
{
    private readonly CliCommandLookupService _sut = new();

    // Minimal fake root used by tests below so they don't depend on the real command tree.
    [CliCommand(Description = "Root", Children = new[] { typeof(VisibleChild), typeof(IgnoredChild) })]
    private class FakeRoot { public void Run() { } }

    [CliCommand(Name = "visible", Description = "Visible leaf")]
    private class VisibleChild { public void Run() { } }

    [McpIgnore]
    [CliCommand(Name = "ignored", Description = "Ignored leaf")]
    private class IgnoredChild { public void Run() { } }

    [CliCommand(Description = "Parent with an ignored subtree", Children = new[] { typeof(IgnoredSubChild) })]
    [McpIgnore]
    private class IgnoredParentWithChildren { public void Run() { } }

    [CliCommand(Name = "sub", Description = "Sub-child of ignored parent")]
    private class IgnoredSubChild { public void Run() { } }

    [CliCommand(Description = "Root with ignored subtree", Children = new[] { typeof(IgnoredParentWithChildren) })]
    private class FakeRootWithIgnoredSubtree { public void Run() { } }

    [McpToolAnnotations(DestructiveHint = true)]
    [CliCommand(Name = "annotated", Description = "Annotated leaf")]
    private class AnnotatedChild { public void Run() { } }

    [CliCommand(Description = "Root with annotated child", Children = new[] { typeof(AnnotatedChild), typeof(VisibleChild) })]
    private class FakeRootWithAnnotatedChild { public void Run() { } }

    [Fact]
    public void EnumerateAllCommands_ExcludesIgnoredLeaf()
    {
        var tools = _sut.EnumerateAllCommands(typeof(FakeRoot)).ToList();

        Assert.Contains(tools, t => t.Name == "visible");
        Assert.DoesNotContain(tools, t => t.Name == "ignored");
    }

    [Fact]
    public void EnumerateAllCommands_ExcludesEntireIgnoredSubtree()
    {
        var tools = _sut.EnumerateAllCommands(typeof(FakeRootWithIgnoredSubtree)).ToList();

        // The ignored parent's child must not appear either.
        Assert.DoesNotContain(tools, t => t.Name.Contains("sub"));
    }

    [Fact]
    public void EnumerateAllCommands_PopulatesAnnotationsFromAttribute()
    {
        var tools = _sut.EnumerateAllCommands(typeof(FakeRootWithAnnotatedChild)).ToList();

        var annotated = tools.FirstOrDefault(t => t.Name == "annotated");
        Assert.NotNull(annotated);
        Assert.NotNull(annotated!.Annotations);
        Assert.True(annotated.Annotations!.DestructiveHint);

        // Non-annotated tool should have null annotations.
        var visible = tools.FirstOrDefault(t => t.Name == "visible");
        Assert.NotNull(visible);
        Assert.Null(visible!.Annotations);
    }

    [Fact]
    public void EnumerateAllCommands_RealTree_ExcludesKnownHiddenCommands()
    {
        var sut = new CliCommandLookupService();
        var tools = sut.EnumerateAllCommands(typeof(TxcCliCommand)).Select(t => t.Name).ToList();

        // Commands that still carry [McpIgnore] must not appear.
        Assert.DoesNotContain(tools, n => n.EndsWith("_start"));               // transform server start
        Assert.DoesNotContain("config_auth_login", tools);                     // interactive browser flow
        Assert.DoesNotContain("config_clear", tools);                          // nuclear config wipe

        // Previously ignored commands that are now visible with annotations.
        Assert.Contains("config_auth_delete", tools);
        Assert.Contains("config_connection_delete", tools);
        Assert.Contains("config_profile_delete", tools);
        Assert.Contains("config_profile_update", tools);
        Assert.Contains("config_profile_validate", tools);
        Assert.Contains("config_profile_pin", tools);
        Assert.Contains("config_profile_unpin", tools);
        Assert.Contains("environment_solution_uninstall", tools);
        Assert.Contains("environment_package_uninstall", tools);

        // Core tools must still be present.
        Assert.Contains("config_profile_create", tools);
        Assert.Contains("config_profile_select", tools);
        Assert.Contains("config_auth_add-service-principal", tools);
        Assert.Contains("workspace_explain", tools);
    }

    [Fact]
    public void EnumerateAllCommands_RealTree_DestructiveToolsHaveAnnotations()
    {
        var sut = new CliCommandLookupService();
        var tools = sut.EnumerateAllCommands(typeof(TxcCliCommand)).ToList();

        var destructiveTools = new[]
        {
            "config_auth_delete",
            "config_connection_delete",
            "config_profile_delete",
            "config_profile_unpin",
            "environment_solution_uninstall",
            "environment_package_uninstall",
        };

        foreach (var name in destructiveTools)
        {
            var tool = tools.FirstOrDefault(t => t.Name == name);
            Assert.NotNull(tool);
            Assert.NotNull(tool!.Annotations);
            Assert.True(tool.Annotations!.DestructiveHint,
                $"Tool '{name}' should have DestructiveHint = true");
        }
    }
}
