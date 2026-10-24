using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class PowerPlatformEnvironmentApiEndpointsTests
{
    [Fact]
    public void BuildBaseUri_SplitsTheLastTwoHexCharactersIntoAShardLabel()
    {
        var uri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(
            Guid.Parse("AABBCCDD-1122-3344-5566-77889900EEFF"), CloudInstance.Public);

        Assert.Equal("https://aabbccdd11223344556677889900ee.ff.environment.api.powerplatform.com/", uri.ToString());
    }

    [Theory]
    [InlineData(CloudInstance.GccHigh)]
    [InlineData(CloudInstance.Dod)]
    [InlineData(CloudInstance.China)]
    public void BuildBaseUri_FailsLoudlyForUnverifiedClouds(CloudInstance cloud)
    {
        Assert.Throws<NotSupportedException>(
            () => PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(Guid.NewGuid(), cloud));
    }
}
