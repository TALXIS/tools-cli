using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse.Runtime.Scopes;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class ResourceScopeTests
{
    [Fact]
    public void Build_Dataverse_KeepsDoubleSlash()
    {
        Assert.Equal(
            "https://contoso.crm.dynamics.com//.default",
            ResourceScope.Build(new Uri("https://contoso.crm.dynamics.com/")));
    }

    [Fact]
    public void Build_PowerPlatformApi_KeepsDoubleSlash()
    {
        Assert.Equal(
            "https://api.powerplatform.com//.default",
            ResourceScope.Build(new Uri("https://api.powerplatform.com/")));
    }

    [Theory]
    [InlineData(CloudInstance.Public, "https://apihub.azure.com/.default")]
    [InlineData(CloudInstance.Gcc, "https://power-apis-usgov001-public.azure-apihub.us/.default")]
    [InlineData(CloudInstance.GccHigh, "https://power-apis-usgov001-sovereign.azure-apihub.us/.default")]
    [InlineData(CloudInstance.Dod, "https://power-apis-usdod-001.azure-apihub.us/.default")]
    [InlineData(CloudInstance.China, "https://china.azure-apihub.cn/.default")]
    public void Build_ApiHub_UsesSingleSlashForEveryCloud(CloudInstance cloud, string expected)
    {
        Assert.Equal(expected, ResourceScope.Build(ApiHubAudiences.For(cloud)));
    }

    [Fact]
    public void Build_ApiHub_IgnoresTrailingSlashAndPath()
    {
        Assert.Equal(
            "https://apihub.azure.com/.default",
            ResourceScope.Build(new Uri("https://apihub.azure.com/")));
        Assert.Equal(
            "https://apihub.azure.com/.default",
            ResourceScope.Build(new Uri("https://apihub.azure.com/apim/office365users/abc/testconnection")));
    }

    [Fact]
    public void IsApiHub_IsFalseForOtherHosts()
    {
        Assert.False(ApiHubAudiences.IsApiHub(new Uri("https://apihub.azure.com.evil.example/")));
        Assert.False(ApiHubAudiences.IsApiHub(new Uri("https://service.powerapps.com/")));
    }
}
