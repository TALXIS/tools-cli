using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse.Authority;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class DataverseCloudMapTests
{
    [Theory]
    [InlineData(CloudInstance.Public, "https://login.microsoftonline.com")]
    [InlineData(CloudInstance.Gcc, "https://login.microsoftonline.com")]
    [InlineData(CloudInstance.GccHigh, "https://login.microsoftonline.us")]
    [InlineData(CloudInstance.Dod, "https://login.microsoftonline.us")]
    [InlineData(CloudInstance.China, "https://login.partner.microsoftonline.cn")]
    public void GetAuthorityHost_MatchesPacMapping(CloudInstance cloud, string expected)
    {
        Assert.Equal(expected, DataverseCloudMap.GetAuthorityHost(cloud));
    }

    [Fact]
    public void BuildAuthorityUri_UsesOrganizations_WhenTenantMissing()
    {
        var uri = DataverseCloudMap.BuildAuthorityUri(CloudInstance.Public, null);
        Assert.Equal("https://login.microsoftonline.com/organizations", uri.AbsoluteUri);
    }

    [Fact]
    public void BuildAuthorityUri_UsesTenant_WhenProvided()
    {
        var uri = DataverseCloudMap.BuildAuthorityUri(CloudInstance.GccHigh, "contoso.onmicrosoft.us");
        Assert.Equal("https://login.microsoftonline.us/contoso.onmicrosoft.us", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://contoso.crm.dynamics.com/", CloudInstance.Public)]
    [InlineData("https://contoso.crm9.dynamics.com/", CloudInstance.Gcc)]
    [InlineData("https://contoso.crm.microsoftdynamics.us/", CloudInstance.GccHigh)]
    [InlineData("https://contoso.crm.dynamics.us/", CloudInstance.GccHigh)]
    [InlineData("https://contoso.crm.appsplatform.us/", CloudInstance.Dod)]
    [InlineData("https://contoso.crm.dynamics.cn/", CloudInstance.China)]
    public void TryInferFromEnvironmentUrl_MatchesKnownSuffixes(string url, CloudInstance expected)
    {
        var inferred = DataverseCloudMap.TryInferFromEnvironmentUrl(new Uri(url));
        Assert.Equal(expected, inferred);
    }

    [Fact]
    public void TryInferFromEnvironmentUrl_ReturnsNull_ForUnknownHost()
    {
        Assert.Null(DataverseCloudMap.TryInferFromEnvironmentUrl(new Uri("https://example.com/")));
    }
}
