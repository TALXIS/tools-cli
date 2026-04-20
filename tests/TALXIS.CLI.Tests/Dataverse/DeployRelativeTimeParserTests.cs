using TALXIS.CLI.Dataverse;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class DeployRelativeTimeParserTests
{
    [Theory]
    [InlineData("30m", 30, 0, 0)]
    [InlineData("24h", 0, 24, 0)]
    [InlineData("7d", 0, 0, 7)]
    [InlineData("2w", 0, 0, 14)]
    [InlineData("1H", 0, 1, 0)]
    [InlineData(" 15m ", 15, 0, 0)]
    public void TryParse_ReturnsExpectedSpan(string input, int minutes, int hours, int days)
    {
        Assert.True(DeployRelativeTimeParser.TryParse(input, out var result));
        var expected = TimeSpan.FromMinutes(minutes) + TimeSpan.FromHours(hours) + TimeSpan.FromDays(days);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("30")]
    [InlineData("-5h")]
    [InlineData("0h")]
    [InlineData("30s")]
    [InlineData("h")]
    public void TryParse_RejectsInvalidInput(string? input)
    {
        Assert.False(DeployRelativeTimeParser.TryParse(input, out _));
    }
}
