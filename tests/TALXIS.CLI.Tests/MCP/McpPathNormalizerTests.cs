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

        var result = McpPathNormalizer.NormalizeOperationalPath(input);

        var expected = string.IsNullOrWhiteSpace(home)
            ? Path.GetFullPath(input)
            : input == "~"
                ? Path.GetFullPath(home)
                : Path.GetFullPath(Path.Combine(home, "Sources", "project"));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("C:~")]
    [InlineData("C:/~")]
    [InlineData("C:\\~")]
    [InlineData("C:/~/Sources/project")]
    [InlineData("C:\\~\\Sources\\project")]
    [InlineData("c:/~")]
    [InlineData("c:/~/Sources/project")]
    [InlineData("c:\\~\\Sources\\project")]
    public void NormalizeOperationalPath_DriveQualifiedTilde_RemainsFilesystemPath(string input)
    {
        var result = McpPathNormalizer.NormalizeOperationalPath(input);

        Assert.Equal(Path.GetFullPath(input), result);
    }

    [Theory]
    [InlineData("C:~folder", false)]
    [InlineData("C:/~folder", false)]
    [InlineData("C:\\~folder", false)]
    [InlineData("/C:/~folder", true)]
    [InlineData("/c:/~folder", true)]
    public void NormalizeOperationalPath_NonDelimitedDriveQualifiedTilde_RemainsFilesystemPath(string input, bool isFileUriLocalDrivePath)
    {
        var result = McpPathNormalizer.NormalizeOperationalPath(input);
        var expected = OperatingSystem.IsWindows() && isFileUriLocalDrivePath
            ? Path.GetFullPath(input[1..])
            : Path.GetFullPath(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/~/Sources/project")]
    [InlineData("\\~\\Sources\\project")]
    public void NormalizeOperationalPath_FileUriLocalHomePath_ResolvesAgainstUserProfile(string input)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        var result = McpPathNormalizer.NormalizeOperationalPath(input, allowFileUriLocalPathHome: true);

        var expected = string.IsNullOrWhiteSpace(home)
            ? Path.GetFullPath(input)
            : Path.GetFullPath(Path.Combine(home, "Sources", "project"));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("/C:/Users/project")]
    [InlineData("/c:/Users/project")]
    public void NormalizeOperationalPath_FileUriDrivePath_NormalizesLeadingSlash(string input)
    {
        var result = McpPathNormalizer.NormalizeOperationalPath(input);
        var expected = OperatingSystem.IsWindows()
            ? Path.GetFullPath(input[1..])
            : Path.GetFullPath(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void NormalizeOperationalPath_VsCodeLowercaseDriveUri_IsValidOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var localPath = "/c:/Users/example-user/Sources/project";
        var result = McpPathNormalizer.NormalizeOperationalPath(localPath, allowFileUriLocalPathHome: true);
        Assert.True(Path.IsPathRooted(result));
        Assert.DoesNotContain("c:\\c:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFullPath("c:/Users/example-user/Sources/project"), result);
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
