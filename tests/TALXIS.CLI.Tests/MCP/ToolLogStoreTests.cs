using Xunit;
using TALXIS.CLI.MCP;

namespace TALXIS.CLI.Tests.MCP;

public class ToolLogStoreTests
{
    [Fact]
    public void Store_ReturnsUriWithToolName()
    {
        var store = new ToolLogStore();
        var uri = store.Store("data_package_import", "full log", "error summary", isError: true);

        Assert.StartsWith(ToolLogStore.UriScheme + "data_package_import/", uri);
    }

    [Fact]
    public void TryGet_ReturnsStoredEntry()
    {
        var store = new ToolLogStore();
        var uri = store.Store("my_tool", "full log content", "errors here", isError: true);

        Assert.True(store.TryGet(uri, out var entry));
        Assert.NotNull(entry);
        Assert.Equal("my_tool", entry.ToolName);
        Assert.Equal("full log content", entry.FullLog);
        Assert.Equal("errors here", entry.ErrorSummary);
        Assert.True(entry.IsError);
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
        store.Store("tool_a", "log a", "", isError: false);
        store.Store("tool_b", "log b", "err", isError: true);

        var all = store.ListAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Store_EvictsOldestWhenOverCapacity()
    {
        var store = new ToolLogStore(maxEntries: 3);
        var uri1 = store.Store("tool", "log1", "", isError: false);
        store.Store("tool", "log2", "", isError: false);
        store.Store("tool", "log3", "", isError: false);
        store.Store("tool", "log4", "", isError: false); // Should evict uri1

        Assert.False(store.TryGet(uri1, out _));
        Assert.Equal(3, store.ListAll().Count);
    }

    [Fact]
    public void Store_GeneratesUniqueUris()
    {
        var store = new ToolLogStore();
        var uri1 = store.Store("tool", "log1", "", isError: false);
        var uri2 = store.Store("tool", "log2", "", isError: false);

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
