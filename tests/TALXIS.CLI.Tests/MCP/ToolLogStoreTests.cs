using Xunit;
using TALXIS.CLI.MCP;

namespace TALXIS.CLI.Tests.MCP;

public class ToolLogStoreTests
{
    private static List<RedactedLogEntry> MakeEntries(params string[] messages) =>
        messages.Select((m, i) => new RedactedLogEntry(
            $"2026-05-04T10:00:{i:D2}Z", "Error", "TestCategory", m)).ToList();

    [Fact]
    public void Store_ReturnsUriWithToolName()
    {
        var store = new ToolLogStore();
        var entries = MakeEntries("schema error");
        var uri = store.Store("data_package_import", 1, "summary", "error summary", entries);

        Assert.StartsWith(ToolLogStore.UriScheme + "data_package_import/", uri);
    }

    [Fact]
    public void TryGet_ReturnsStoredEntry()
    {
        var store = new ToolLogStore();
        var entries = new List<RedactedLogEntry>
        {
            new("2026-05-04T10:00:00Z", "Error", "TestCategory", "schema error"),
            new("2026-05-04T10:00:01Z", "Warning", "TestCategory", "minor issue")
        };
        var uri = store.Store("my_tool", 1, "summary", "errors here", entries);

        Assert.True(store.TryGet(uri, out var entry));
        Assert.NotNull(entry);
        Assert.Equal("my_tool", entry.ToolName);
        Assert.Equal(1, entry.ExitCode);
        Assert.Equal("summary", entry.PrimaryText);
        Assert.Equal("errors here", entry.ErrorSummary);
        Assert.Equal(2, entry.LogEntries.Count);
        Assert.Equal("schema error", entry.LogEntries[0].Message);
        Assert.Equal("minor issue", entry.LogEntries[1].Message);
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
        store.Store("tool_a", 1, "summary a", "", MakeEntries("log a"));
        store.Store("tool_b", 1, "summary b", "err", MakeEntries("log b"));

        var all = store.ListAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Store_EvictsOldestWhenOverCapacity()
    {
        var store = new ToolLogStore(maxEntries: 3);
        var uri1 = store.Store("tool", 1, "summary1", "", MakeEntries("log1"));
        store.Store("tool", 1, "summary2", "", MakeEntries("log2"));
        store.Store("tool", 1, "summary3", "", MakeEntries("log3"));
        store.Store("tool", 1, "summary4", "", MakeEntries("log4")); // Should evict uri1

        Assert.False(store.TryGet(uri1, out _));
        Assert.Equal(3, store.ListAll().Count);
    }

    [Fact]
    public void Store_GeneratesUniqueUris()
    {
        var store = new ToolLogStore();
        var uri1 = store.Store("tool", 1, "summary1", "", MakeEntries("log1"));
        var uri2 = store.Store("tool", 1, "summary2", "", MakeEntries("log2"));

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
