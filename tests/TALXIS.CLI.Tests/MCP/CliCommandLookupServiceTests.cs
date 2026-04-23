using DotMake.CommandLine;
using TALXIS.CLI.MCP;
using TALXIS.CLI.Shared;
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
    public void EnumerateAllCommands_RealTree_ExcludesKnownHiddenCommands()
    {
        var sut = new CliCommandLookupService();
        var tools = sut.EnumerateAllCommands(typeof(TxcCliCommand)).Select(t => t.Name).ToList();

        // Commands that carry [McpIgnore] must not appear.
        Assert.DoesNotContain(tools, n => n.EndsWith("_start"));               // transform server start
        Assert.DoesNotContain("config_auth_login", tools);
        Assert.DoesNotContain("config_auth_delete", tools);
        Assert.DoesNotContain("config_connection_delete", tools);
        Assert.DoesNotContain("config_profile_delete", tools);
        Assert.DoesNotContain("config_profile_update", tools);
        Assert.DoesNotContain("config_profile_validate", tools);
        Assert.DoesNotContain("config_profile_pin", tools);
        Assert.DoesNotContain("config_profile_unpin", tools);

        // Core tools must still be present.
        Assert.Contains("config_profile_create", tools);
        Assert.Contains("config_profile_select", tools);
        Assert.Contains("config_auth_add-service-principal", tools);
        Assert.Contains("workspace_explain", tools);
    }
}
