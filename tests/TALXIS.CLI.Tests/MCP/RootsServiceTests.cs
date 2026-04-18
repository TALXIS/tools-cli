using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class RootsServiceTests
{
    [Fact]
    public void ConvertFileUri_UnixPath_ReturnsLocalPath()
    {
        var result = RootsService.ConvertFileUriToPath("file:///home/user/project");
        Assert.Equal("/home/user/project", result);
    }

    [Fact]
    public void ConvertFileUri_WindowsPath_ReturnsLocalPath()
    {
        var result = RootsService.ConvertFileUriToPath("file:///C:/Users/project");
        Assert.NotNull(result);
        // On Unix, Uri.LocalPath returns "/C:/Users/project"; on Windows, "C:\Users\project"
        Assert.EndsWith("C:/Users/project", result.Replace('\\', '/'));
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
        Assert.Equal("/home/user/my project", result);
    }
}
