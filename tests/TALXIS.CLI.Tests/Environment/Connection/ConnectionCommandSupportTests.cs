using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connection;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionCommandSupportTests
{
    [Fact]
    public void TryParsePairs_SplitsOnTheFirstEqualsSign()
    {
        var ok = ConnectionCommandSupport.TryParsePairs(
            ["token:clientId=abc", "note=a=b=c"], "--parameter", out var result, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("abc", result["token:clientId"]);
        Assert.Equal("a=b=c", result["note"]);
    }

    [Theory]
    [InlineData("novalue")]
    [InlineData("=value")]
    public void TryParsePairs_RejectsAnItemWithoutAName(string item)
    {
        var ok = ConnectionCommandSupport.TryParsePairs([item], "--parameter", out _, out var error);

        Assert.False(ok);
        Assert.Contains("name=value", error);
    }

    [Fact]
    public void TryParsePairs_RejectsADuplicateNameIgnoringCase()
    {
        var ok = ConnectionCommandSupport.TryParsePairs(["a=1", "A=2"], "--parameter", out _, out var error);

        Assert.False(ok);
        Assert.Contains("more than once", error);
    }

    [Fact]
    public void TryParsePairs_AcceptsNothing()
    {
        Assert.True(ConnectionCommandSupport.TryParsePairs(null, "--parameter", out var result, out _));
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(null, ConnectionPermissionRole.CanView)]
    [InlineData("can-view", ConnectionPermissionRole.CanView)]
    [InlineData("CanViewWithShare", ConnectionPermissionRole.CanViewWithShare)]
    [InlineData("can_view_with_share", ConnectionPermissionRole.CanViewWithShare)]
    [InlineData("CAN-EDIT", ConnectionPermissionRole.CanEdit)]
    public void TryParseRole_AcceptsKebabPascalAndSnakeCase(string? text, ConnectionPermissionRole expected)
    {
        Assert.True(ConnectionCommandSupport.TryParseRole(text, out var role));
        Assert.Equal(expected, role);
    }

    [Fact]
    public void TryParseRole_RejectsUnknownRoles()
        => Assert.False(ConnectionCommandSupport.TryParseRole("can-use", out _));

    [Theory]
    [InlineData("CanView", "can-view")]
    [InlineData("CanViewWithShare", "can-view-with-share")]
    [InlineData("CanEdit", "can-edit")]
    [InlineData("Owner", "owner")]
    public void FormatRole_UsesTheCliVocabulary(string platform, string expected)
        => Assert.Equal(expected, ConnectionCommandSupport.FormatRole(platform));

    [Fact]
    public void TryBuildPrincipal_NeedsExactlyOne()
    {
        Assert.False(ConnectionCommandSupport.TryBuildPrincipal(null, null, null, out _, out var none));
        Assert.Contains("exactly one", none);
        Assert.False(ConnectionCommandSupport.TryBuildPrincipal("u@x.com", "app", null, out _, out _));

        Assert.True(ConnectionCommandSupport.TryBuildPrincipal(null, "app-id", null, out var spn, out _));
        Assert.Equal(new ConnectionPrincipalSpec(ConnectionPrincipalKind.ServicePrincipal, "app-id"), spn);
        Assert.True(ConnectionCommandSupport.TryBuildPrincipal(null, null, "grp", out var group, out _));
        Assert.Equal(ConnectionPrincipalKind.Group, group!.Kind);
    }
}
