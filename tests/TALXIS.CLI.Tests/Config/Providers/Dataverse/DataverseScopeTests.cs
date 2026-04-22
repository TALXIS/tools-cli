using TALXIS.CLI.Config.Providers.Dataverse.Scopes;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class DataverseScopeTests
{
    [Fact]
    public void BuildDefault_UsesDoubleSlash_ForDataverseAudience()
    {
        var scope = DataverseScope.BuildDefault(new Uri("https://contoso.crm.dynamics.com/"));
        Assert.Equal("https://contoso.crm.dynamics.com//.default", scope);
    }

    [Fact]
    public void BuildDefault_StripsPathAndQuery()
    {
        var scope = DataverseScope.BuildDefault(new Uri("https://contoso.crm.dynamics.com/api/data/v9.2?foo=bar"));
        Assert.Equal("https://contoso.crm.dynamics.com//.default", scope);
    }

    [Fact]
    public void BuildDefault_PreservesNonStandardPort()
    {
        var scope = DataverseScope.BuildDefault(new Uri("https://dv.local:8443/"));
        Assert.Equal("https://dv.local:8443//.default", scope);
    }
}
