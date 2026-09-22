using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class PowerPlatformEnvironmentApiEndpointsTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("0123456789abcdef0123456789abcdef");

    [Fact]
    public void BuildBaseUri_SplitsTheGuidIntoAPrefixAndAShardLabel()
    {
        var uri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(EnvironmentId, CloudInstance.Public);

        Assert.Equal(
            "https://0123456789abcdef0123456789abcd.ef.environment.api.powerplatform.com/",
            uri.ToString());
    }

    [Fact]
    public void BuildBaseUri_FromAName_MatchesTheGuidOverload()
    {
        var fromGuid = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(EnvironmentId, CloudInstance.Public);
        var fromName = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(EnvironmentId.ToString(), CloudInstance.Public);

        Assert.Equal(fromGuid, fromName);
    }

    [Fact]
    public void BuildBaseUri_ADefaultEnvironmentGetsTheDefaultPrefix()
    {
        // A tenant's default environment is named "Default-{guid}" and its host
        // carries that prefix; without it the request goes nowhere.
        var uri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri($"Default-{EnvironmentId}", CloudInstance.Public);

        Assert.Equal(
            "https://default0123456789abcdef0123456789abcd.ef.environment.api.powerplatform.com/",
            uri.ToString());
    }

    [Fact]
    public void BuildBaseUri_TheDefaultPrefixIsMatchedRegardlessOfCase()
    {
        var uri = PowerPlatformEnvironmentApiEndpoints.BuildBaseUri($"default-{EnvironmentId}", CloudInstance.Public);

        Assert.StartsWith("https://default0123", uri.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CloudInstance.Public, "environment.api.powerplatform.com")]
    // GCC shares the commercial host, matching how the BAP control plane treats it.
    [InlineData(CloudInstance.Gcc, "environment.api.powerplatform.com")]
    [InlineData(CloudInstance.GccHigh, "environment.api.high.powerplatform.microsoft.us")]
    [InlineData(CloudInstance.Dod, "environment.api.appsplatform.us")]
    public void GetDomainSuffix_MapsEachSupportedCloud(CloudInstance cloud, string expected)
        => Assert.Equal(expected, PowerPlatformEnvironmentApiEndpoints.GetDomainSuffix(cloud));

    [Fact]
    public void GetDomainSuffix_AnUnmappedCloudFailsLoudly()
        => Assert.Throws<NotSupportedException>(
            () => PowerPlatformEnvironmentApiEndpoints.GetDomainSuffix(CloudInstance.China));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("My Production Environment")]
    [InlineData("Default-not-a-guid")]
    public void BuildBaseUri_RejectsAnythingThatIsNotAnEnvironmentId(string name)
        => Assert.Throws<ArgumentException>(
            () => PowerPlatformEnvironmentApiEndpoints.BuildBaseUri(name, CloudInstance.Public));
}
