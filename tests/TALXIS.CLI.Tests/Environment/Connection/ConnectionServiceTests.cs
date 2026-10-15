using System.Net;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionServiceTests
{
    private const string Catalog = "connectivity/connectors?";
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

    private static FakePlatform Platform(string connectorApiId, string connectorDefinition, string existingConnections = "{\"value\":[]}")
        => new FakePlatform()
            .On(Get, Catalog, HttpStatusCode.OK, ConnectionFixtures.Catalog())
            .On(Get, ConnectionList, HttpStatusCode.OK, existingConnections)
            .On(Get, $"connectivity/connectors/{connectorApiId}?", HttpStatusCode.OK, connectorDefinition);

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task Create_NoAuthCustomConnector_ResolvesTheLogicalNameAndCreates()
    {
        var platform = Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Connected("new", ConnectionFixtures.CustomApiId, "Food Facts"));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var outcome = await service.CreateAsync(
            null,
            new ConnectionCreateOptions(ConnectionFixtures.CustomLogicalName, "Food Facts", null, NoParameters(), true),
            CancellationToken.None);

        Assert.Equal("created", outcome.Status);
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
        var platform = Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector())
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
        var platform = Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector(), existing);
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var outcome = await service.CreateAsync(
            null,
            new ConnectionCreateOptions(ConnectionFixtures.CustomLogicalName, "food facts", null, NoParameters(), true),
            CancellationToken.None);

        Assert.Equal("unchanged", outcome.Status);
        Assert.Equal("abc123", outcome.ConnectionId);
        Assert.Empty(platform.Sent(Put, "/connections/"));
    }

    [Fact]
    public async Task Create_TwoConnectionsWithTheSameName_IsAValidationError()
    {
        var existing = ConnectionFixtures.Wrap(
            ConnectionFixtures.Connected("a", ConnectionFixtures.CustomApiId, "Food Facts"),
            ConnectionFixtures.Connected("b", ConnectionFixtures.CustomApiId, "Food Facts"));
        var platform = Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector(), existing);
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
        var platform = Platform(ConnectionFixtures.CustomApiId, ConnectionFixtures.NoAuthConnector(), ConnectionFixtures.Wrap(broken))
            .On(Put, "/connections/keepme", HttpStatusCode.OK, ConnectionFixtures.Connected("keepme", ConnectionFixtures.CustomApiId, "Food Facts"));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var outcome = await service.CreateAsync(
            null,
            new ConnectionCreateOptions(ConnectionFixtures.CustomLogicalName, "Food Facts", null, NoParameters(), true),
            CancellationToken.None);

        Assert.Equal("updated", outcome.Status);
        Assert.Equal("keepme", outcome.ConnectionId);
    }

    [Fact]
    public async Task Create_UnknownConnector_ListsHowToFindOne()
    {
        var platform = Platform("x", "{}");
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            null, new ConnectionCreateOptions("nope", "N", null, NoParameters(), true), CancellationToken.None));

        Assert.Contains("connector list", ex.Message);
    }

    // ------------------------------------------------------ service principal

    [Fact]
    public async Task Create_ServicePrincipalMethod_SendsAParameterSet()
    {
        var platform = Platform("shared_commondataserviceforapps", ConnectionFixtures.DataverseConnector())
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

        Assert.Equal("created", outcome.Status);
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
        var platform = Platform("shared_commondataserviceforapps", ConnectionFixtures.DataverseConnector())
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

        Assert.Equal("failed", outcome.Status);
        Assert.Contains("AADSTS700016", outcome.Message);
        Assert.DoesNotContain("TOPSECRET", outcome.Message);
        Assert.Single(platform.Sent(Delete, "/connections/"));
    }

    [Fact]
    public async Task Create_UnknownParameter_IsRejectedBeforeAnythingIsSent()
    {
        var platform = Platform("shared_commondataserviceforapps", ConnectionFixtures.DataverseConnector());
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

    // ----------------------------------------------------------------- consent

    [Fact]
    public async Task Create_OAuthConnector_CompletesConsentSilentlyForAUser()
    {
        var tokens = new FakeTokens();
        var platform = Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true))
            .On(Post, "firstPartyLogin", HttpStatusCode.OK)
            .On(Get, "/connections/", HttpStatusCode.OK, ConnectionFixtures.Connected("c1", "shared_office365users", "Mail"));
        var service = ConnectionFixtures.Service(platform, tokens, ConnectionFixtures.User());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("shared_office365users", "Mail", null, NoParameters(), true), CancellationToken.None);

        Assert.Equal("created", outcome.Status);
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
        var platform = Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Unauthenticated("c1", "shared_office365users", false));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("shared_office365users", "Mail", null, NoParameters(), true), CancellationToken.None);

        Assert.Equal("consent-required", outcome.Status);
        Assert.StartsWith("https://usa.consent.azure-apihub.net/login", outcome.ConsentLink);
        Assert.Empty(platform.Sent(Post, "firstPartyLogin"));
        Assert.Empty(platform.Sent(Delete, "/connections/"));
    }

    [Fact]
    public async Task Create_OAuthConnectorForAServicePrincipal_NeverAttemptsConsent()
    {
        var tokens = new FakeTokens();
        var platform = Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true));
        var service = ConnectionFixtures.Service(platform, tokens, ConnectionFixtures.ServicePrincipal());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("shared_office365users", "Mail", null, NoParameters(), true), CancellationToken.None);

        Assert.Equal("consent-required", outcome.Status);
        Assert.Contains("user credential", outcome.Message);
        Assert.Empty(platform.Sent(Post, "firstPartyLogin"));
        Assert.DoesNotContain(ApiHubAudiences.For(CloudInstance.Public), tokens.Audiences);
    }

    [Fact]
    public async Task Create_WithConsentDisabled_ReturnsTheLinkWithoutAttemptingIt()
    {
        var platform = Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Put, "/connections/", HttpStatusCode.Created, ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.CreateAsync(
            null, new ConnectionCreateOptions("shared_office365users", "Mail", null, NoParameters(), AttemptConsent: false), CancellationToken.None);

        Assert.Equal("consent-required", outcome.Status);
        Assert.Empty(platform.Sent(Post, "firstPartyLogin"));
    }

    // -------------------------------------------------------------------- list

    [Fact]
    public async Task List_SendsTheCallersObjectIdAndFiltersByConnectorAndName()
    {
        var all = ConnectionFixtures.Wrap(
            ConnectionFixtures.Connected("a", ConnectionFixtures.CustomApiId, "Food Facts"),
            ConnectionFixtures.Connected("b", "shared_office365users", "Mail"));
        var platform = Platform("x", "{}", all);
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User(), LogicalNames);

        var byConnector = await service.ListAsync(null, ConnectionFixtures.CustomLogicalName, null, CancellationToken.None);
        var byName = await service.ListAsync(null, null, "mail", CancellationToken.None);

        Assert.Equal("a", Assert.Single(byConnector).ConnectionId);
        Assert.Equal("b", Assert.Single(byName).ConnectionId);
        var listing = platform.Sent(Get, ConnectionList).First();
        Assert.Contains(FakeTokens.ObjectId, Uri.UnescapeDataString(listing.Uri.Query));
    }

    // ------------------------------------------------------------- permissions

    private const string SpnAppId = "11ec2176-c65e-4c53-83af-c8638cd4347b";
    private const string SpnObjectId = "25a5f191-c862-4e69-a24f-643a6b73bd46";

    private static string Permissions(string? spnRole)
    {
        const string owner = """{"name":"9fe11834-e8c9-4387-9854-2aad4296e231","properties":{"roleName":"Owner","principal":{"id":"9fe11834-e8c9-4387-9854-2aad4296e231","type":"User"}}}""";
        var spn = spnRole is null
            ? string.Empty
            : ",{\"name\":\"9c5dbf38-1a9e-4306-94b3-2c3ef5df67d7\",\"properties\":{\"roleName\":\"" + spnRole
              + "\",\"principal\":{\"id\":\"" + SpnObjectId + "\",\"type\":\"ServicePrincipal\"}}}";
        return "{\"value\":[" + owner + spn + "]}";
    }

    private static string Graph() => "{\"value\":[{\"id\":\"" + SpnObjectId + "\",\"appId\":\"" + SpnAppId + "\",\"displayName\":\"deploy-spn\"}]}";

    private static FakePlatform PermissionPlatform(string spnRole)
        => Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Get, "/permissions?", HttpStatusCode.OK, Permissions(spnRole))
            .On(Post, "modifyPermissions", HttpStatusCode.OK)
            .On(Get, "servicePrincipals", HttpStatusCode.OK, Graph());

    private static ConnectionPrincipalSpec Spn => new(ConnectionPrincipalKind.ServicePrincipal, SpnAppId);

    [Fact]
    public async Task AddPermission_NewPrincipal_PutsTheObjectIdResolvedFromTheClientId()
    {
        var calls = 0;
        var platform = PermissionPlatform("CanView")
            .On(Get, "/permissions?", _ => ++calls == 1
                ? FakePlatform.Response(HttpStatusCode.OK, Permissions(null))
                : FakePlatform.Response(HttpStatusCode.OK, Permissions("CanView")));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanView, CancellationToken.None);

        Assert.Equal("assigned", outcome.Status);
        Assert.Equal("CanView", outcome.Permission!.Role);
        var post = Assert.Single(platform.Sent(Post, "modifyPermissions"));
        Assert.Contains(SpnObjectId, post.Body);
        Assert.DoesNotContain(SpnAppId, post.Body);
        Assert.Contains("\"roleName\":\"CanView\"", post.Body);
    }

    [Fact]
    public async Task AddPermission_SameRole_IsUnchangedAndSendsNothing()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanView, CancellationToken.None);

        Assert.Equal("unchanged", outcome.Status);
        Assert.Empty(platform.Sent(Post, "modifyPermissions"));
    }

    [Fact]
    public async Task AddPermission_DifferentRole_IsUpdated()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanEdit, CancellationToken.None);

        Assert.Equal("updated", outcome.Status);
        Assert.Single(platform.Sent(Post, "modifyPermissions"));
    }

    [Fact]
    public async Task AddPermission_ForTheOwner_IsRejected()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.AddPermissionAsync(
            null, "shared_office365users", "c1",
            new ConnectionPrincipalSpec(ConnectionPrincipalKind.User, "9fe11834-e8c9-4387-9854-2aad4296e231"),
            ConnectionPermissionRole.CanView, CancellationToken.None));

        Assert.Contains("owns this connection", ex.Message);
    }

    [Fact]
    public async Task AddPermission_UserByEmail_SendsTheEmailNotAnId()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        await service.AddPermissionAsync(
            null, "shared_office365users", "c1",
            new ConnectionPrincipalSpec(ConnectionPrincipalKind.User, "dev@contoso.com"),
            ConnectionPermissionRole.CanView, CancellationToken.None);

        var post = Assert.Single(platform.Sent(Post, "modifyPermissions"));
        Assert.Contains("\"email\":\"dev@contoso.com\"", post.Body);
        Assert.Contains("\"type\":\"User\"", post.Body);
    }

    [Fact]
    public async Task RemovePermission_UsesThePermissionIdNotThePrincipalId()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.RemovePermissionAsync(null, "shared_office365users", "c1", Spn, CancellationToken.None);

        Assert.Equal("removed", outcome.Status);
        var post = Assert.Single(platform.Sent(Post, "modifyPermissions"));
        Assert.Contains("/permissions/9c5dbf38-1a9e-4306-94b3-2c3ef5df67d7", post.Body);
        Assert.DoesNotContain($"/permissions/{SpnObjectId}", post.Body);
    }

    [Fact]
    public async Task RemovePermission_NotShared_IsUnchanged()
    {
        var platform = Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Get, "/permissions?", HttpStatusCode.OK, Permissions(null))
            .On(Get, "servicePrincipals", HttpStatusCode.OK, Graph());
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.RemovePermissionAsync(null, "shared_office365users", "c1", Spn, CancellationToken.None);

        Assert.Equal("unchanged", outcome.Status);
        Assert.Null(outcome.Permission);
        Assert.Empty(platform.Sent(Post, "modifyPermissions"));
    }

    [Fact]
    public async Task RemovePermission_OfTheOwner_IsRejected()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RemovePermissionAsync(
            null, "shared_office365users", "c1",
            new ConnectionPrincipalSpec(ConnectionPrincipalKind.User, "9fe11834-e8c9-4387-9854-2aad4296e231"),
            CancellationToken.None));

        Assert.Contains("cannot be removed", ex.Message);
    }

    [Fact]
    public async Task AddPermission_ServicePrincipalWhenGraphIsUnavailable_FallsBackToTheValueAsObjectId()
    {
        var platform = PermissionPlatform("CanView")
            .On(Get, "servicePrincipals", HttpStatusCode.Forbidden, "{}")
            .On(Get, "/permissions?", HttpStatusCode.OK, Permissions(null));
        var service = ConnectionFixtures.Service(platform, new FakeTokens(), ConnectionFixtures.User());

        await service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanView, CancellationToken.None);

        var post = Assert.Single(platform.Sent(Post, "modifyPermissions"));
        Assert.Contains(SpnAppId, post.Body);
    }
}
