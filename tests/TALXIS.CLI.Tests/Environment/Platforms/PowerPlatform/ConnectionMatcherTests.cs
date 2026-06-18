using TALXIS.CLI.Core.Deployment;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.PowerPlatform;

public class ConnectionMatcherTests
{
    private static ConnectionReferenceSetting Ref(string logicalName, string? connectionId) =>
        new() { LogicalName = logicalName, ConnectionId = connectionId };

    [Fact]
    public void FindMissing_ReturnsEmpty_WhenConnectionExists()
    {
        var missing = ConnectionMatcher.FindMissing(
            [Ref("new_o365", "f3d887a13d0d4faba017870352e3efce")],
            ["f3d887a13d0d4faba017870352e3efce"]);

        Assert.Empty(missing);
    }

    [Fact]
    public void FindMissing_MatchesRegardlessOfHyphensAndCase()
    {
        // settings file: no hyphens; environment returns dashed + different case
        var missing = ConnectionMatcher.FindMissing(
            [Ref("new_o365", "f3d887a13d0d4faba017870352e3efce")],
            ["F3D887A1-3D0D-4FAB-A017-870352E3EFCE"]);

        Assert.Empty(missing);
    }

    [Fact]
    public void FindMissing_FlagsConnectionAbsentFromEnvironment()
    {
        var missing = ConnectionMatcher.FindMissing(
            [Ref("new_o365", "00000000000000000000000000000000")],
            ["f3d887a13d0d4faba017870352e3efce"]);

        var entry = Assert.Single(missing);
        Assert.Contains("new_o365", entry);
        Assert.Contains("00000000000000000000000000000000", entry);
    }

    [Fact]
    public void FindMissing_IgnoresReferencesWithoutConnectionId()
    {
        var missing = ConnectionMatcher.FindMissing(
            [Ref("new_o365", ""), Ref("new_sp", null)],
            []);

        Assert.Empty(missing);
    }

    [Fact]
    public void FindMissing_FlagsEachMissingReference()
    {
        var missing = ConnectionMatcher.FindMissing(
            [Ref("new_a", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), Ref("new_b", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")],
            ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]);

        var entry = Assert.Single(missing);
        Assert.Contains("new_b", entry);
    }
}
