using Xunit;
using TALXIS.CLI.MCP;

namespace TALXIS.CLI.Tests.MCP;

public class ToolLogStoreTests
{
    [Fact]
    public void Store_ReturnsUriWithToolName()
    {
        var store = new ToolLogStore();
        var uri = store.StoreFailure("data_package_import", 1, "summary", "error summary", "full log");

        Assert.StartsWith(ToolLogStore.UriScheme + "data_package_import/", uri);
    }

    [Fact]
    public void TryGet_ReturnsStoredEntry()
    {
        var store = new ToolLogStore();
        var uri = store.StoreFailure("my_tool", 1, "summary", "errors here", "full log content");

        Assert.True(store.TryGet(uri, out var entry));
        Assert.NotNull(entry);
        Assert.Equal("my_tool", entry.ToolName);
        Assert.Equal(1, entry.ExitCode);
        Assert.Equal("summary", entry.PrimaryText);
        Assert.Equal("full log content", entry.FullLog);
        Assert.Equal("errors here", entry.ErrorSummary);
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknownUri()
    {
        var store = new ToolLogStore();
        Assert.False(store.TryGet("txc://logs/unknown/abc123", out _));
    }

    [Fact]
    public void ListAll_ReturnsAllEntries()
    {
        var store = new ToolLogStore();
        store.StoreFailure("tool_a", 0, "summary a", "", "log a");
        store.StoreFailure("tool_b", 1, "summary b", "err", "log b");

        var all = store.ListAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Store_EvictsOldestWhenOverCapacity()
    {
        var store = new ToolLogStore(maxEntries: 3);
        var uri1 = store.StoreFailure("tool", 1, "summary1", "", "log1");
        store.StoreFailure("tool", 1, "summary2", "", "log2");
        store.StoreFailure("tool", 1, "summary3", "", "log3");
        store.StoreFailure("tool", 1, "summary4", "", "log4"); // Should evict uri1

        Assert.False(store.TryGet(uri1, out _));
        Assert.Equal(3, store.ListAll().Count);
    }

    [Fact]
    public void Store_GeneratesUniqueUris()
    {
        var store = new ToolLogStore();
        var uri1 = store.StoreFailure("tool", 1, "summary1", "", "log1");
        var uri2 = store.StoreFailure("tool", 1, "summary2", "", "log2");

        Assert.NotEqual(uri1, uri2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ThrowsForNonPositiveMaxEntries(int maxEntries)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolLogStore(maxEntries));
    }
}
