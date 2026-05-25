using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class McpPathNormalizerTests
{
    [Theory]
    [InlineData("~")]
    [InlineData("~/Sources/project")]
    [InlineData("~\\Sources\\project")]
    public void NormalizeOperationalPath_HomeRelativeInput_ResolvesAgainstUserProfile(string input)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrWhiteSpace(home));

        var result = McpPathNormalizer.NormalizeOperationalPath(input);

        var expected = input == "~"
            ? Path.GetFullPath(home)
            : Path.GetFullPath(Path.Combine(home, "Sources", "project"));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/~/Sources/project")]
    [InlineData("\\~\\Sources\\project")]
    public void NormalizeOperationalPath_FileUriLocalHomePath_ResolvesAgainstUserProfile(string input)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrWhiteSpace(home));

        var result = McpPathNormalizer.NormalizeOperationalPath(input, allowFileUriLocalPathHome: true);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(home, "Sources", "project")),
            result);
    }

    [Fact]
    public void NormalizeOperationalPath_AbsoluteUnixPath_RemainsAbsolute()
    {
        var result = McpPathNormalizer.NormalizeOperationalPath("/tmp/project");
        Assert.Equal(Path.GetFullPath("/tmp/project"), result);
    }

    [Fact]
    public void ExpandHomeRelativePath_WithoutFileUriFlag_LeavesSlashTildePathUnchanged()
    {
        const string input = "/~/Sources/project";

        var result = McpPathNormalizer.ExpandHomeRelativePath(input);

        Assert.Equal(input, result);
    }
}
