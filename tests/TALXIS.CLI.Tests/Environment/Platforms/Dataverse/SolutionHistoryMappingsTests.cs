using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Features.Environment;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionHistoryMappingsTests
{
    [Theory]
    [InlineData(1, "Install")]
    [InlineData(2, "HoldingImport")]
    [InlineData(3, "Update")]
    [InlineData(5, "Upgrade")]
    public void MapSuboperation_ReturnsReadableLabel(int code, string expected)
    {
        Assert.Equal(expected, SolutionHistoryMappings.MapSuboperation(code));
    }

    [Fact]
    public void MapSuboperation_NullIsUnknown()
    {
        Assert.Equal("Unknown", SolutionHistoryMappings.MapSuboperation(null));
    }

    [Fact]
    public void MapSuboperation_UnmappedCodeIsLabelled()
    {
        Assert.Equal("Unknown(99)", SolutionHistoryMappings.MapSuboperation(99));
    }

    [Theory]
    [InlineData(1, "Import")]
    [InlineData(2, "Uninstall")]
    public void MapOperation_ReturnsReadableLabel(int code, string expected)
    {
        Assert.Equal(expected, SolutionHistoryMappings.MapOperation(code));
    }
}
