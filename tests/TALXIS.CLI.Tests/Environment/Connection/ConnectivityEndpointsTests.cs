using TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectivityEndpointsTests
{
    private static readonly Uri Base = new("https://abc.de.environment.api.powerplatform.com/");
    private static readonly Guid Env = new("72946668-bfcc-e9a8-9056-b023c2381946");

    [Fact]
    public void Connection_PutHasNoExpandUnlessConsentIsWanted()
    {
        Assert.DoesNotContain("expand", ConnectivityEndpoints.Connection(Base, "shared_x", "c1", false).Query);
        Assert.Contains("$expand=ConsentLink", ConnectivityEndpoints.Connection(Base, "shared_x", "c1", true).Query);
    }

    [Fact]
    public void Connections_AlwaysNamesTheCallerInThePermissionsExpand()
    {
        var uri = ConnectivityEndpoints.Connections(Base, Env, "0cf68951-a282-40e9-960c-a35ad855a60e");

        var decoded = Uri.UnescapeDataString(uri.Query);
        Assert.Contains("environment eq '72946668-bfcc-e9a8-9056-b023c2381946'", decoded);
        Assert.Contains("permissions($filter=maxAssignedTo('0cf68951-a282-40e9-960c-a35ad855a60e'))", decoded);
    }

    [Fact]
    public void ConnectionRoutes_KeepTheConnectionIdVerbatim_AndEscapeSegments()
    {
        var uri = ConnectivityEndpoints.ModifyPermissions(Base, "shared_a b", "pac-dv-1234", Env);

        Assert.Equal("/connectivity/connectors/shared_a%20b/connections/pac-dv-1234/modifyPermissions", uri.AbsolutePath);
        Assert.Contains("api-version=1", uri.Query);
    }

    [Fact]
    public void Audience_IsThePowerPlatformApi()
        => Assert.Equal("https://api.powerplatform.com/", ConnectivityEndpoints.Audience.ToString());
}
