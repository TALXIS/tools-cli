using TALXIS.CLI.Features.Environment.Diagnostics;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Diagnostics;

public class FollowSupportTests
{
    [Fact]
    public void TryParseInterval_DefaultsTo5s_WhenEmpty()
    {
        Assert.True(FollowSupport.TryParseInterval(null, out var interval, out _));
        Assert.Equal(TimeSpan.FromSeconds(5), interval);
    }

    [Fact]
    public void TryParseInterval_ParsesSeconds()
    {
        Assert.True(FollowSupport.TryParseInterval("10", out var interval, out _));
        Assert.Equal(TimeSpan.FromSeconds(10), interval);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1")]
    public void TryParseInterval_RejectsBadOrTooSmall(string value)
    {
        Assert.False(FollowSupport.TryParseInterval(value, out _, out var error));
        Assert.NotNull(error);
    }
}

public class FollowTrackerTests
{
    [Fact]
    public void SelectNew_FirstBatch_ReturnsAllOldestFirst()
    {
        var tracker = new FollowTracker<string>(s => s);
        // readers return newest-first
        var fresh = tracker.SelectNew(new[] { "b", "a" });

        Assert.Equal(new[] { "a", "b" }, fresh);
    }

    [Fact]
    public void SelectNew_SecondBatch_ReturnsOnlyUnseen()
    {
        var tracker = new FollowTracker<string>(s => s);
        tracker.SelectNew(new[] { "b", "a" });

        var fresh = tracker.SelectNew(new[] { "c", "b", "a" });

        Assert.Equal(new[] { "c" }, fresh);
    }

    [Fact]
    public void SelectNew_SkipsItemsWithEmptyKey()
    {
        var tracker = new FollowTracker<string>(_ => null);

        var fresh = tracker.SelectNew(new[] { "x", "y" });

        Assert.Empty(fresh);
    }
}
