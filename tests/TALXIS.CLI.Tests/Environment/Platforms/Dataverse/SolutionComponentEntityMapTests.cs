using TALXIS.CLI.Core.Contracts.Dataverse;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionComponentEntityMapTests
{
    [Theory]
    [InlineData(20, "role")]                          // security role (the issue's case)
    [InlineData(91, "pluginassembly")]
    [InlineData(92, "sdkmessageprocessingstep")]
    [InlineData(61, "webresource")]
    [InlineData(380, "environmentvariabledefinition")]
    public void TryGetEntityLogicalName_MapsRecordBackedTypes(int componentType, string expected)
    {
        Assert.True(SolutionComponentEntityMap.TryGetEntityLogicalName(componentType, out var entity));
        Assert.Equal(expected, entity);
        Assert.True(SolutionComponentEntityMap.IsSupported(componentType));
    }

    [Theory]
    [InlineData(1)]   // Entity (metadata table) - intentionally unsupported
    [InlineData(2)]   // Attribute (metadata column)
    [InlineData(9)]   // OptionSet
    [InlineData(99999)]
    public void TryGetEntityLogicalName_RejectsMetadataAndUnknownTypes(int componentType)
    {
        Assert.False(SolutionComponentEntityMap.TryGetEntityLogicalName(componentType, out var entity));
        Assert.Null(entity);
        Assert.False(SolutionComponentEntityMap.IsSupported(componentType));
    }

    [Fact]
    public void SupportedSummary_ListsKnownEntities()
    {
        var summary = SolutionComponentEntityMap.SupportedSummary;
        Assert.Contains("role", summary);
        Assert.Contains("sdkmessageprocessingstep", summary);
    }
}
