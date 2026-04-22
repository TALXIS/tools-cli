using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Environment;
using TALXIS.CLI.Environment.Platforms.Dataverse;
using Xunit;

namespace TALXIS.CLI.Tests.Deploy.Services;

public class DeployIdSelectorTests
{
    [Fact]
    public void Parse_LatestKeyword_ReturnsLatest()
    {
        var s = DeployIdSelector.Parse("latest");
        Assert.Equal(DeployIdSelectorKind.Latest, s.Kind);
    }

    [Fact]
    public void Parse_LatestKeyword_CaseInsensitive()
    {
        var s = DeployIdSelector.Parse("LATEST");
        Assert.Equal(DeployIdSelectorKind.Latest, s.Kind);
    }

    [Fact]
    public void Parse_FullGuid_ReturnsGuid()
    {
        var g = Guid.NewGuid();
        var s = DeployIdSelector.Parse(g.ToString());
        Assert.Equal(DeployIdSelectorKind.Guid, s.Kind);
        Assert.Equal(g, s.Guid);
    }

    [Theory]
    [InlineData("MySolution")]
    [InlineData("PCT21011-StrongerCalendar")]
    [InlineData("publisher_MyApp")]
    [InlineData("9de18071")]
    [InlineData("9DE18071")]
    [InlineData("9de18071-2838")]
    public void Parse_FreeText_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => DeployIdSelector.Parse(input));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("xyz1")]
    public void Parse_ShortOrNonHex_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => DeployIdSelector.Parse(input));
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeployIdSelector.Parse("   "));
    }
}
