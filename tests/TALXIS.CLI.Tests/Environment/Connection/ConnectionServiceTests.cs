using System.Net;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionServiceTests
{
    private const string ConnectionList = "connectivity/connections?";
    private static readonly HttpMethod Get = HttpMethod.Get;
    private static readonly HttpMethod Put = HttpMethod.Put;
    private static readonly HttpMethod Post = HttpMethod.Post;
    private static readonly HttpMethod Delete = HttpMethod.Delete;

    private static IReadOnlyDictionary<string, string> LogicalNames => new Dictionary<string, string>
    {
        [ConnectionFixtures.CustomLogicalName] = ConnectionFixtures.CustomApiId,
    };

    private static Dictionary<string, string> NoParameters() => new();

    [Fact]
    public async Task Create_NoAuthCustomConnector_ResolvesTheLogicalNameAndCreates()
    {
        var platform = ConnectionFixtures.Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Connected("new", ConnectionFixtures.CustomApiId, "Food Facts"));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var outcome = await service.CreateAsync(
            null,
            new ConnectionCreateOptions(ConnectionFixtures.CustomLogicalName, "Food Facts", null, NoParameters(), true),
            CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Created, outcome.Status);
        Assert.Equal("Connected", outcome.ConnectionStatus);
        Assert.Equal(ConnectionFixtures.CustomApiId, outcome.ApiId);

        var put = Assert.Single(platform.Sent(Put, "/connections/"));
        Assert.Contains($"/connectors/{ConnectionFixtures.CustomApiId}/connections/", put.Uri.AbsolutePath);
        Assert.DoesNotContain("expand", put.Uri.Query);
        Assert.DoesNotContain("consentInfo", put.Body);
        Assert.Contains("\"displayName\":\"Food Facts\"", put.Body);
    }

    [Fact]
    public async Task Create_AcceptsAnExactDisplayName()
    {
        var platform = ConnectionFixtures.Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Connected("new", ConnectionFixtures.CustomApiId, "Food Facts"));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("open food facts", "Food Facts", null, NoParameters(), true), CancellationToken.None);

        Assert.Equal(ConnectionFixtures.CustomApiId, outcome.ApiId);
    }

    [Fact]
    public async Task Create_ExistingConnectedConnection_IsUnchangedAndNothingIsSent()
    {
        var existing = ConnectionFixtures.Wrap(ConnectionFixtures.Connected("abc123", ConnectionFixtures.CustomApiId, "Food Facts"));
        var platform = ConnectionFixtures.Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector(), existing);
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var outcome = await service.CreateAsync(
            null,
            new ConnectionCreateOptions(ConnectionFixtures.CustomLogicalName, "food facts", null, NoParameters(), true),
            CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Unchanged, outcome.Status);
        Assert.Equal("abc123", outcome.ConnectionId);
        Assert.Empty(platform.Sent(Put, "/connections/"));
    }

    [Fact]
    public async Task Create_TwoConnectionsWithTheSameName_IsAValidationError()
    {
        var existing = ConnectionFixtures.Wrap(
            ConnectionFixtures.Connected("a", ConnectionFixtures.CustomApiId, "Food Facts"),
            ConnectionFixtures.Connected("b", ConnectionFixtures.CustomApiId, "Food Facts"));
        var platform = ConnectionFixtures.Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector(), existing);
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            null,
            new ConnectionCreateOptions(ConnectionFixtures.CustomLogicalName, "Food Facts", null, NoParameters(), true),
            CancellationToken.None));

        Assert.Contains("a, b", ex.Message);
    }

    [Fact]
    public async Task Create_ExistingBrokenConnection_IsUpdatedInPlace()
    {
        var broken = """
            {"name":"keepme","properties":{"apiId":"/providers/Microsoft.PowerApps/apis/shared_almlab-5fopen-20food-20facts-5fa7d7fde51b746a91",
             "displayName":"Food Facts","statuses":[{"status":"Error","error":{"code":"Unauthorized","message":"old"}}]}}
            """;
        var platform = ConnectionFixtures.Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector(), ConnectionFixtures.Wrap(broken))
            .On(Put, "/connections/keepme", HttpStatusCode.OK, ConnectionFixtures.Connected("keepme", ConnectionFixtures.CustomApiId, "Food Facts"));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var outcome = await service.CreateAsync(
            null,
            new ConnectionCreateOptions(ConnectionFixtures.CustomLogicalName, "Food Facts", null, NoParameters(), true),
            CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Updated, outcome.Status);
        Assert.Equal("keepme", outcome.ConnectionId);
    }

    [Fact]
    public async Task Create_UnknownConnector_IsAConnectorNotFound()
    {
        var platform = ConnectionFixtures.Platform("x", "{}");
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var ex = await Assert.ThrowsAsync<ConnectorNotFoundException>(() => service.CreateAsync(
            null, new ConnectionCreateOptions("nope", "N", null, NoParameters(), true), CancellationToken.None));

        Assert.Contains("nope", ex.Message);
    }


    [Fact]
    public async Task Create_ServicePrincipalMethod_SendsAParameterSet()
    {
        var platform = ConnectionFixtures.Platform("shared_commondataserviceforapps", ConnectionFixtures.DataverseConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Connected("n", "shared_commondataserviceforapps", "Dv"));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.ServicePrincipal());

        var outcome = await service.CreateAsync(
            null,
            new ConnectionCreateOptions(
                "shared_commondataserviceforapps", "Dv", "ServicePrincipalOauth",
                new Dictionary<string, string>
                {
                    ["token:clientId"] = "app-id",
                    ["token:clientSecret"] = "s3cret-value",
                    ["token:TenantId"] = "tenant-id",
                },
                true),
            CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Created, outcome.Status);
        var put = Assert.Single(platform.Sent(Put, "/connections/"));
        Assert.Contains("\"connectionParametersSet\"", put.Body);
        Assert.Contains("\"name\":\"ServicePrincipalOauth\"", put.Body);
        Assert.DoesNotContain("consentInfo", put.Body);
        Assert.DoesNotContain("expand", put.Uri.Query);
        Assert.DoesNotContain("s3cret-value", outcome.ToString());
    }

    [Fact]
    public async Task Create_RejectedCredentials_FailWithThePlatformMessage_AndRemoveTheNewConnection()
    {
        const string rejected = """
            {"name":"n","properties":{"apiId":"/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps","displayName":"Dv",
             "statuses":[{"status":"Error","target":"token","error":{"code":"Unauthorized","message":"AADSTS700016: Application was not found in the directory."}}]}}
            """;
        var platform = ConnectionFixtures.Platform("shared_commondataserviceforapps", ConnectionFixtures.DataverseConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, rejected)
            .On(Delete, "/connections/", HttpStatusCode.OK);
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.ServicePrincipal());

        var outcome = await service.CreateAsync(
            null,
            new ConnectionCreateOptions(
                "shared_commondataserviceforapps", "Dv", "ServicePrincipalOauth",
                new Dictionary<string, string> { ["token:clientId"] = "a", ["token:clientSecret"] = "TOPSECRET", ["token:TenantId"] = "t" },
                true),
            CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Failed, outcome.Status);
        Assert.Contains("AADSTS700016", outcome.Message);
        Assert.DoesNotContain("TOPSECRET", outcome.Message);
        Assert.Single(platform.Sent(Delete, "/connections/"));
    }

    [Fact]
    public async Task Create_UnknownParameter_IsRejectedBeforeAnythingIsSent()
    {
        var platform = ConnectionFixtures.Platform("shared_commondataserviceforapps", ConnectionFixtures.DataverseConnector());
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.ServicePrincipal());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            null,
            new ConnectionCreateOptions(
                "shared_commondataserviceforapps", "Dv", "ServicePrincipalOauth",
                new Dictionary<string, string> { ["bogus"] = "x" }, true),
            CancellationToken.None));

        Assert.Contains("bogus", ex.Message);
        Assert.Empty(platform.Sent(Put, "/connections/"));
    }


    [Fact]
    public async Task Create_OAuthConnector_CompletesConsentSilentlyForAUser()
    {
        var tokens = new FakeTokens();
        var platform = ConnectionFixtures.Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true))
            .On(Post, "firstPartyLogin", HttpStatusCode.OK)
            .On(Get, "/connections/", HttpStatusCode.OK, ConnectionFixtures.Connected("c1", "shared_office365users", "Mail"));
        var service = ConnectionFixtures.Service(platform, tokens, ConnectionFixtures.User());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("shared_office365users", "Mail", null, NoParameters(), true), CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Created, outcome.Status);
        Assert.Equal("Connected", outcome.ConnectionStatus);

        var put = Assert.Single(platform.Sent(Put, "/connections/"));
        Assert.Contains("$expand=ConsentLink", put.Uri.Query);
        Assert.Contains("\"redirectUrl\"", put.Body);

        var consent = Assert.Single(platform.Sent(Post, "firstPartyLogin"));
        Assert.Contains("\"accessToken\":\"header.", consent.Body);
        Assert.False(consent.HadAuthorization, "the consent service rejects requests that carry an Authorization header");
        Assert.Contains(ApiHubAudiences.For(CloudInstance.Public), tokens.Audiences);
    }

    [Fact]
    public async Task Create_OAuthConnectorWithoutOnBehalfOfSupport_ReturnsTheConsentLink_AndKeepsTheConnection()
    {
        var platform = ConnectionFixtures.Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Unauthenticated("c1", "shared_office365users", false));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("shared_office365users", "Mail", null, NoParameters(), true), CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.ConsentRequired, outcome.Status);
        Assert.StartsWith("https://usa.consent.azure-apihub.net/login", outcome.ConsentLink);
        Assert.Empty(platform.Sent(Post, "firstPartyLogin"));
        Assert.Empty(platform.Sent(Delete, "/connections/"));
    }

    [Fact]
    public async Task Create_OAuthConnectorForAServicePrincipal_NeverAttemptsConsent()
    {
        var tokens = new FakeTokens();
        var platform = ConnectionFixtures.Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true));
        var service = ConnectionFixtures.Service(platform, tokens, ConnectionFixtures.ServicePrincipal());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("shared_office365users", "Mail", null, NoParameters(), true), CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.ConsentRequired, outcome.Status);
        Assert.Contains("user credential", outcome.Message);
        Assert.Empty(platform.Sent(Post, "firstPartyLogin"));
        Assert.DoesNotContain(ApiHubAudiences.For(CloudInstance.Public), tokens.Audiences);
    }

    [Fact]
    public async Task Create_WithConsentDisabled_ReturnsTheLinkWithoutAttemptingIt()
    {
        var platform = ConnectionFixtures.Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("shared_office365users", "Mail", null, NoParameters(), AttemptConsent: false), CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.ConsentRequired, outcome.Status);
        Assert.Empty(platform.Sent(Post, "firstPartyLogin"));
    }


    [Fact]
    public async Task List_SendsTheCallersObjectIdAndFiltersByConnectorAndName()
    {
        var all = ConnectionFixtures.Wrap(
            ConnectionFixtures.Connected("a", ConnectionFixtures.CustomApiId, "Food Facts"),
            ConnectionFixtures.Connected("b", "shared_office365users", "Mail"));
        var platform = ConnectionFixtures.Platform("x", "{}", all);
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var byConnector = await service.ListAsync(null, ConnectionFixtures.CustomLogicalName, null, CancellationToken.None);
        var byName = await service.ListAsync(null, null, "mail", CancellationToken.None);

        Assert.Equal("a", Assert.Single(byConnector).ConnectionId);
        Assert.Equal("b", Assert.Single(byName).ConnectionId);
        var listing = platform.Sent(Get, ConnectionList).First();
        Assert.Contains(FakeTokens.ObjectId, Uri.UnescapeDataString(listing.Uri.Query));
    }
}
