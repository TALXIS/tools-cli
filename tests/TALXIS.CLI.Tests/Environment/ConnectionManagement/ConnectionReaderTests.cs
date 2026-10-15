using System.Net;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionReaderTests
{
    private static readonly HttpMethod Get = HttpMethod.Get;
    private const string ConnectionRoute = "/connectors/shared_office365users/connections/c1?";

    private static IConnectionService Service(FakePlatform platform)
        => ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

    private static FakePlatform Platform()
        => ConnectionFixtures.Platform("shared_office365users", ConnectionFixtures.OAuthConnector());

    [Fact]
    public async Task Get_ReadsTheConnection()
    {
        var platform = Platform().On(Get, ConnectionRoute, HttpStatusCode.OK,
            ConnectionFixtures.Connected("c1", "shared_office365users", "Mail"));

        var summary = await Service(platform).GetAsync(null, "shared_office365users", "c1", CancellationToken.None);

        Assert.Equal("Mail", summary.DisplayName);
        Assert.Equal("shared_office365users", summary.ApiId);
    }

    [Fact]
    public async Task Get_AMissingConnection_IsAConnectionNotFound()
    {
        var platform = Platform().On(Get, ConnectionRoute, HttpStatusCode.NotFound, "{}");

        var ex = await Assert.ThrowsAsync<ConnectionNotFoundException>(
            () => Service(platform).GetAsync(null, "shared_office365users", "c1", CancellationToken.None));

        Assert.Equal("c1", ex.ConnectionId);
        Assert.Equal("shared_office365users", ex.ApiId);
    }

    [Fact]
    public async Task Get_AConnectionTheCallerMayNotRead_IsAnAccessDenied()
    {
        var platform = Platform().On(Get, ConnectionRoute, HttpStatusCode.Forbidden, "{}");

        await Assert.ThrowsAsync<ConnectionAccessDeniedException>(
            () => Service(platform).GetAsync(null, "shared_office365users", "c1", CancellationToken.None));
    }

    [Fact]
    public async Task List_A404ForTheRouteItself_IsNotPresentedAsAValidationError()
    {
        // The list route does not name a connection or a connector, so a 404
        // there is a platform fault, not bad input.
        var platform = Platform().On(Get, "connectivity/connections?", HttpStatusCode.NotFound, "{}");

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => Service(platform).ListAsync(null, null, null, CancellationToken.None));

        Assert.IsNotAssignableFrom<ArgumentException>(ex);
    }

    [Fact]
    public async Task List_FollowsNextLinkAcrossPages()
    {
        var page1 = "{\"value\":[" + ConnectionFixtures.Connected("a", "shared_office365users", "A")
            + "],\"nextLink\":\"https://abc.environment.api.powerplatform.com/connectivity/connections?page=2\"}";
        var page2 = ConnectionFixtures.Wrap(ConnectionFixtures.Connected("b", "shared_office365users", "B"));
        var platform = Platform()
            .On(Get, "connectivity/connections?page=2", HttpStatusCode.OK, page2)
            .On(Get, "connectivity/connections?api-version", HttpStatusCode.OK, page1);

        var all = await Service(platform).ListAsync(null, null, null, CancellationToken.None);

        Assert.Equal(["a", "b"], all.Select(c => c.ConnectionId));
    }

    [Fact]
    public async Task List_StopsAtThePageCap()
    {
        const string endless = "{\"value\":[],\"nextLink\":\"https://abc.environment.api.powerplatform.com/connectivity/connections?again=1\"}";
        var platform = Platform()
            .On(Get, "connectivity/connections?", HttpStatusCode.OK, endless);

        var all = await Service(platform).ListAsync(null, null, null, CancellationToken.None);

        Assert.Empty(all);
        Assert.Equal(20, platform.Sent(Get, "connectivity/connections?").Count());
    }

    [Fact]
    public async Task Test_ASilentlyConnectedConnectionWithoutATestLink_IsHealthy()
    {
        var platform = Platform().On(Get, ConnectionRoute, HttpStatusCode.OK,
            ConnectionFixtures.Connected("c1", "shared_office365users", "Mail"));

        var outcome = await Service(platform).TestAsync(null, "shared_office365users", "c1", CancellationToken.None);

        Assert.True(outcome.Healthy);
    }

    [Fact]
    public async Task Test_AnUnauthenticatedConnection_IsReportedWithThePlatformMessage()
    {
        var platform = Platform().On(Get, ConnectionRoute, HttpStatusCode.OK,
            ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true));

        var outcome = await Service(platform).TestAsync(null, "shared_office365users", "c1", CancellationToken.None);

        Assert.False(outcome.Healthy);
        Assert.Equal("This connection is not authenticated.", outcome.Message);
    }

    [Fact]
    public async Task Delete_AMissingConnection_IsAConnectionNotFound()
    {
        var platform = Platform().On(HttpMethod.Delete, "/connections/c1", HttpStatusCode.NotFound, "{}");

        await Assert.ThrowsAsync<ConnectionNotFoundException>(
            () => Service(platform).DeleteAsync(null, "shared_office365users", "c1", CancellationToken.None));
    }

    private static string TwoConnectionsWithTheSameId() => ConnectionFixtures.Wrap(
        ConnectionFixtures.Connected("c1", "shared_office365users", "Mail"),
        ConnectionFixtures.Connected("c1", "shared_commondataserviceforapps", "Dataverse"));

    [Fact]
    public async Task Get_WithoutAConnector_FindsTheConnectionById()
    {
        var platform = Platform()
            .On(Get, "connectivity/connections?", HttpStatusCode.OK,
                ConnectionFixtures.Wrap(ConnectionFixtures.Connected("c1", "shared_office365users", "Mail")))
            .On(Get, ConnectionRoute, HttpStatusCode.OK, ConnectionFixtures.Connected("c1", "shared_office365users", "Mail"));

        var summary = await Service(platform).GetAsync(null, null, "c1", CancellationToken.None);

        Assert.Equal("shared_office365users", summary.ApiId);
    }

    [Fact]
    public async Task Get_WithoutAConnector_AnUnknownId_IsAConnectionNotFoundWithoutAConnector()
    {
        var platform = Platform();

        var ex = await Assert.ThrowsAsync<ConnectionNotFoundException>(
            () => Service(platform).GetAsync(null, null, "c1", CancellationToken.None));

        Assert.Equal("c1", ex.ConnectionId);
        Assert.Null(ex.ApiId);
        Assert.DoesNotContain("for connector", ex.Message);
    }

    [Fact]
    public async Task Get_WithoutAConnector_AnIdUnderSeveralConnectors_IsAmbiguous()
    {
        var platform = Platform().On(Get, "connectivity/connections?", HttpStatusCode.OK, TwoConnectionsWithTheSameId());

        var ex = await Assert.ThrowsAsync<AmbiguousConnectionException>(
            () => Service(platform).GetAsync(null, null, "c1", CancellationToken.None));

        Assert.Equal(["shared_commondataserviceforapps", "shared_office365users"], ex.ApiIds.Order().ToArray());
    }

    [Fact]
    public async Task Get_NamingTheConnector_DisambiguatesWithoutListing()
    {
        var platform = Platform()
            .On(Get, "connectivity/connections?", HttpStatusCode.OK, TwoConnectionsWithTheSameId())
            .On(Get, ConnectionRoute, HttpStatusCode.OK, ConnectionFixtures.Connected("c1", "shared_office365users", "Mail"));

        var summary = await Service(platform).GetAsync(null, "shared_office365users", "c1", CancellationToken.None);

        Assert.Equal("Mail", summary.DisplayName);
        Assert.Empty(platform.Sent(Get, "connectivity/connections?"));
    }

    [Fact]
    public async Task Test_WithoutAConnector_FindsTheConnectionById()
    {
        var platform = Platform()
            .On(Get, "connectivity/connections?", HttpStatusCode.OK,
                ConnectionFixtures.Wrap(ConnectionFixtures.Connected("c1", "shared_office365users", "Mail")))
            .On(Get, ConnectionRoute, HttpStatusCode.OK, ConnectionFixtures.Connected("c1", "shared_office365users", "Mail"));

        var outcome = await Service(platform).TestAsync(null, null, "c1", CancellationToken.None);

        Assert.True(outcome.Healthy);
    }

    [Fact]
    public async Task Delete_WithoutAConnector_DeletesTheConnectionFoundById()
    {
        var platform = Platform()
            .On(Get, "connectivity/connections?", HttpStatusCode.OK,
                ConnectionFixtures.Wrap(ConnectionFixtures.Connected("c1", "shared_office365users", "Mail")))
            .On(HttpMethod.Delete, ConnectionRoute, HttpStatusCode.OK);

        await Service(platform).DeleteAsync(null, null, "c1", CancellationToken.None);

        Assert.Single(platform.Sent(HttpMethod.Delete, "/connections/c1"));
    }
}
