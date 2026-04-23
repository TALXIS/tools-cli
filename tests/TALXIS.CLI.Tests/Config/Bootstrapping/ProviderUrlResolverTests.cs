using TALXIS.CLI.Core.Bootstrapping;
using TALXIS.CLI.Core.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Bootstrapping;

public class ProviderUrlResolverTests
{
    [Theory]
    [InlineData("https://contoso.crm4.dynamics.com/", ProviderKind.Dataverse)]
    [InlineData("https://contoso.crm.dynamics.com", ProviderKind.Dataverse)]
    [InlineData("https://contoso.crm.microsoftdynamics.us", ProviderKind.Dataverse)]
    [InlineData("https://contoso.crm.appsplatform.us", ProviderKind.Dataverse)]
    [InlineData("https://contoso.crm.dynamics.cn", ProviderKind.Dataverse)]
    public void Infer_ResolvesDataverseHosts(string url, ProviderKind expected)
    {
        var r = ProviderUrlResolver.Infer(url);
        Assert.Equal(expected, r.Provider);
        Assert.Null(r.Error);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://contoso.crm4.dynamics.fake")]
    public void Infer_ReturnsErrorForUnknownHost(string url)
    {
        var r = ProviderUrlResolver.Infer(url);
        Assert.Null(r.Provider);
        Assert.NotNull(r.Error);
        Assert.Contains("Known host suffixes", r.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://contoso.crm.dynamics.com")]
    public void Infer_ReturnsErrorForInvalidUrl(string? url)
    {
        var r = ProviderUrlResolver.Infer(url);
        Assert.Null(r.Provider);
        Assert.NotNull(r.Error);
    }

    [Theory]
    [InlineData("https://contoso.crm4.dynamics.com/", "contoso")]
    [InlineData("https://contoso-dev.crm.dynamics.com", "contoso-dev")]
    [InlineData("https://CONTOSO.crm.dynamics.com/", "contoso")]
    [InlineData("https://weird__name.crm.dynamics.com", "weird-name")]
    public void DeriveDefaultName_UsesFirstDnsLabel(string url, string expected)
    {
        Assert.Equal(expected, ProviderUrlResolver.DeriveDefaultName(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public void DeriveDefaultName_ReturnsNullForInvalid(string? url)
    {
        Assert.Null(ProviderUrlResolver.DeriveDefaultName(url));
    }
}
