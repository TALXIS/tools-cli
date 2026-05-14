using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class RootsServiceTests
{
    [Fact]
    public void ConvertFileUri_UnixPath_ReturnsNormalisedPath()
    {
        var result = RootsService.ConvertFileUriToPath("file:///home/user/project");
        // Path.GetFullPath normalises; on Unix the result is unchanged.
        Assert.NotNull(result);
        Assert.Equal("/home/user/project", result.Replace('\\', '/'));
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
        Assert.EndsWith("c:/Users/project", result.Replace('\\', '/'));
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
    public void ConvertFileUri_ResultIsFullPath()
    {
        // Path.GetFullPath always returns an absolute path
        var result = RootsService.ConvertFileUriToPath("file:///home/user/project");
        Assert.NotNull(result);
        Assert.True(Path.IsPathFullyQualified(result),
            $"Expected fully-qualified path but got: {result}");
    }
}
