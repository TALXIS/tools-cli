using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Plugin.Profile;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Plugin;

public class PluginTraceLevelParserTests
{
    [Theory]
    [InlineData("all", PluginTraceLevel.All)]
    [InlineData("ALL", PluginTraceLevel.All)]
    [InlineData("exception", PluginTraceLevel.Exception)]
    [InlineData("exceptions", PluginTraceLevel.Exception)]
    [InlineData("off", PluginTraceLevel.Off)]
    [InlineData("none", PluginTraceLevel.Off)]
    public void TryParse_MapsKnownValues(string value, PluginTraceLevel expected)
    {
        var ok = PluginTraceLevelParser.TryParse(value, PluginTraceLevel.All, out var level, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_EmptyUsesDefault(string? value)
    {
        var ok = PluginTraceLevelParser.TryParse(value, PluginTraceLevel.All, out var level, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(PluginTraceLevel.All, level);
    }

    [Fact]
    public void TryParse_Invalid_ReturnsError()
    {
        var ok = PluginTraceLevelParser.TryParse("verbose", PluginTraceLevel.All, out var level, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("verbose", error);
    }
}
