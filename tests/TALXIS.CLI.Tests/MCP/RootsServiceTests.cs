using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class RootsServiceTests
{
    [Fact]
    public void ConvertFileUri_UnixPath_ReturnsNormalisedPath()
    {
        var result = RootsService.ConvertFileUriToPath("file:///home/user/project");
        Assert.NotNull(result);
        // Path.GetFullPath may prepend a drive root on Windows, so assert the
        // meaningful suffix rather than an exact match.
        Assert.EndsWith("home/user/project", result.Replace('\\', '/'));
    }

    [Fact]
    public void ConvertFileUri_WindowsPath_ReturnsNormalisedPath()
    {
        var result = RootsService.ConvertFileUriToPath("file:///C:/Users/project");
        Assert.NotNull(result);
        // On both platforms, the path must end with C:/Users/project (separator may vary).
        // On Windows: Path.GetFullPath strips leading / and uses backslashes.
        // On Unix: the path stays as-is (no drive letters on Unix).
        Assert.EndsWith("C:/Users/project", result.Replace('\\', '/'));
    }

    [Fact]
    public void ConvertFileUri_WindowsLowercaseDrive_ReturnsNormalisedPath()
    {
        // VS Code on Windows sends lowercase drive letters
        var result = RootsService.ConvertFileUriToPath("file:///c:/Users/project");
        Assert.NotNull(result);
        Assert.True(Path.IsPathFullyQualified(result), $"Expected fully-qualified path but got: {result}");

        var normalised = result.Replace('\\', '/');
        Assert.DoesNotContain("c:/c:/", normalised, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Users/project", normalised, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void ConvertFileUri_LowercaseDriveWorkspaceRoot_DoesNotDuplicateDrive()
    {
        if (!OperatingSystem.IsWindows()) return;

        var result = RootsService.ConvertFileUriToPath("file:///c:/Users/example-user/Sources/my-agent-team");
        Assert.NotNull(result);
        Assert.True(Path.IsPathFullyQualified(result));
        Assert.DoesNotContain(@"c:\c:", result, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Users", "example-user", "Sources", "my-agent-team"), result,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertFileUri_NonFileUri_ReturnsNull()
    {
        var result = RootsService.ConvertFileUriToPath("https://example.com");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ConvertFileUri_EmptyOrNull_ReturnsNull(string? uri)
    {
        var result = RootsService.ConvertFileUriToPath(uri);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFileUri_InvalidUri_ReturnsNull()
    {
        var result = RootsService.ConvertFileUriToPath("not a uri at all");
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFileUri_EncodedSpaces_DecodesCorrectly()
    {
        var result = RootsService.ConvertFileUriToPath("file:///home/user/my%20project");
        Assert.NotNull(result);
        Assert.Contains("my project", result);
    }

    [Fact]
    public void ConvertFileUri_HomeRelativePath_ResolvesAgainstUserProfile()
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var result = RootsService.ConvertFileUriToPath("file:///~/Sources/project");

        Assert.NotNull(result);
        var expected = string.IsNullOrWhiteSpace(home)
            ? Path.GetFullPath("/~/Sources/project")
            : Path.GetFullPath(Path.Combine(home, "Sources", "project"));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("file:///C:/~/Sources/project")]
    [InlineData("file:///c:/~/Sources/project")]
    public void ConvertFileUri_DriveQualifiedHome_RemainsFilesystemPath(string uri)
    {
        var result = RootsService.ConvertFileUriToPath(uri);

        Assert.NotNull(result);
        var expected = OperatingSystem.IsWindows()
            ? Path.GetFullPath(uri.Contains("/c:/", StringComparison.Ordinal) ? "c:/~/Sources/project" : "C:/~/Sources/project")
            : Path.GetFullPath(uri.Contains("/c:/", StringComparison.Ordinal) ? "/c:/~/Sources/project" : "/C:/~/Sources/project");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertFileUri_ResultIsFullPath()
    {
        // Path.GetFullPath always returns an absolute path
        var result = RootsService.ConvertFileUriToPath("file:///home/user/project");
        Assert.NotNull(result);
        Assert.True(Path.IsPathFullyQualified(result),
            $"Expected fully-qualified path but got: {result}");
    }
}
