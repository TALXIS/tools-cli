using System.Net;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionPermissionServiceTests
{
    private static readonly HttpMethod Get = HttpMethod.Get;
    private static readonly HttpMethod Post = HttpMethod.Post;

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
        => ConnectionFixtures.Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
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
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanView, CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Assigned, outcome.Status);
        Assert.Equal(ConnectionPermissionRole.CanView, outcome.Permission!.Role);
        var post = Assert.Single(platform.Sent(Post, "modifyPermissions"));
        Assert.Contains(SpnObjectId, post.Body);
        Assert.DoesNotContain(SpnAppId, post.Body);
        Assert.Contains("\"roleName\":\"CanView\"", post.Body);
    }

    [Fact]
    public async Task AddPermission_SameRole_IsUnchangedAndSendsNothing()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanView, CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Unchanged, outcome.Status);
        Assert.Empty(platform.Sent(Post, "modifyPermissions"));
    }

    [Fact]
    public async Task AddPermission_DifferentRole_IsUpdated()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanEdit, CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Updated, outcome.Status);
        Assert.Single(platform.Sent(Post, "modifyPermissions"));
    }

    [Fact]
    public async Task AddPermission_ForTheOwner_IsRejected()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

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
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

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
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.RemovePermissionAsync(null, "shared_office365users", "c1", Spn, CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Removed, outcome.Status);
        var post = Assert.Single(platform.Sent(Post, "modifyPermissions"));
        Assert.Contains("/permissions/9c5dbf38-1a9e-4306-94b3-2c3ef5df67d7", post.Body);
        Assert.DoesNotContain($"/permissions/{SpnObjectId}", post.Body);
    }

    [Fact]
    public async Task RemovePermission_NotShared_IsUnchanged()
    {
        var platform = ConnectionFixtures.Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Get, "/permissions?", HttpStatusCode.OK, Permissions(null))
            .On(Get, "servicePrincipals", HttpStatusCode.OK, Graph());
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        var outcome = await service.RemovePermissionAsync(null, "shared_office365users", "c1", Spn, CancellationToken.None);

        Assert.Equal(ConnectionOutcomeStatus.Unchanged, outcome.Status);
        Assert.Null(outcome.Permission);
        Assert.Empty(platform.Sent(Post, "modifyPermissions"));
    }

    [Fact]
    public async Task RemovePermission_OfTheOwner_IsRejected()
    {
        var platform = PermissionPlatform("CanView");
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

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
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        await service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanView, CancellationToken.None);

        var post = Assert.Single(platform.Sent(Post, "modifyPermissions"));
        Assert.Contains(SpnAppId, post.Body);
    }

    [Fact]
    public async Task ListPermissions_ReturnsTheOwnerWithTheOwnerRole()
    {
        var platform = PermissionPlatform("CanEdit");
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        var permissions = await service.ListPermissionsAsync(null, "shared_office365users", "c1", CancellationToken.None);

        Assert.Equal([ConnectionPermissionRole.Owner, ConnectionPermissionRole.CanEdit], permissions.Select(p => p.Role));
    }

    [Fact]
    public async Task ListPermissions_AMissingConnection_IsAConnectionNotFound()
    {
        var platform = ConnectionFixtures.Platform("shared_office365users", ConnectionFixtures.OAuthConnector())
            .On(Get, "/permissions?", HttpStatusCode.NotFound, "{}");
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        await Assert.ThrowsAsync<ConnectionNotFoundException>(
            () => service.ListPermissionsAsync(null, "shared_office365users", "c1", CancellationToken.None));
    }

    [Fact]
    public async Task AddPermission_ARefusedShare_IsNotReportedAsAConnectionAccessProblem()
    {
        var platform = PermissionPlatform("CanView")
            .On(Get, "/permissions?", HttpStatusCode.OK, Permissions(null))
            .On(Post, "modifyPermissions", HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\",\"message\":\"nope\"}}");
        var service = ConnectionFixtures.PermissionService(platform, new FakeTokens(), ConnectionFixtures.User());

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.AddPermissionAsync(
            null, "shared_office365users", "c1", Spn, ConnectionPermissionRole.CanView, CancellationToken.None));

        Assert.Contains("nope", ex.Message);
    }
}
