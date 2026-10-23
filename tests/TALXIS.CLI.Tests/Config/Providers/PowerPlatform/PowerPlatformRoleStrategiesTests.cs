using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;
using TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;
using Xunit;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class PowerPlatformRoleStrategiesTests
{
    [Fact]
    public async Task PowerPlatformRbacRoleStrategy_ResolveTenantRoleAsync_RejectsAmbiguousNames()
    {
        var http = new FakeHttpClientFactoryWrapper(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(
        [
            _ => JsonResponse("""
            {
              "value": [
                {
                  "roleDefinitionId": "11111111-1111-1111-1111-111111111111",
                  "roleDefinitionName": "Tenant Admin",
                  "assignableScopes": ["/tenants/tenant-id"]
                },
                {
                  "roleDefinitionId": "22222222-2222-2222-2222-222222222222",
                  "roleDefinitionName": "Tenant Admin",
                  "assignableScopes": ["/tenants/tenant-id"]
                }
              ]
            }
            """)
        ]));

        var strategy = new PowerPlatformRbacRoleStrategy(new PowerPlatformRbacClient(new FakeAccessTokenService(), http));

        await Assert.ThrowsAsync<TenantRoleAmbiguousException>(() =>
            strategy.ResolveTenantRoleAsync(TestConnection(), TestCredential(), "Tenant Admin", CancellationToken.None));
    }

    [Fact]
    public async Task BapAdminApplicationRoleStrategy_RejectsNonApplicationPrincipals()
    {
        var strategy = new BapAdminApplicationRoleStrategy(
            new BapAdminApiClient(new FakeAccessTokenService(), new FakeHttpClientFactoryWrapper(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>())));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            strategy.AddAsync(
                TestConnection(),
                TestCredential(),
                new PowerPlatformRolePrincipalReference(PowerPlatformPrincipalType.User, Guid.NewGuid()),
                BapAdminApplicationRoleStrategy.AdminApplicationRoleValue,
                CancellationToken.None));

        Assert.Contains("only valid for application principals", ex.Message);
    }

    [Fact]
    public async Task BapAdminApplicationRoleStrategy_ListAsync_ReturnsSyntheticAssignment_WhenRegistered()
    {
        var appId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var strategy = new BapAdminApplicationRoleStrategy(
            new BapAdminApiClient(
                new FakeAccessTokenService(),
                new FakeHttpClientFactoryWrapper(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(
                [
                    _ => JsonResponse($"[{{\"applicationId\":\"{appId}\"}}]")
                ]))));

        var assignments = await strategy.ListAsync(
            TestConnection(),
            TestCredential(),
            new PowerPlatformRolePrincipalReference(PowerPlatformPrincipalType.ApplicationUser, Guid.NewGuid(), appId),
            CancellationToken.None);

        var assignment = Assert.Single(assignments);
        Assert.True(assignment.IsSynthetic);
        Assert.Equal(BapAdminApplicationRoleStrategy.AdminApplicationRoleValue, assignment.RoleIdentifier);
    }

    private static Connection TestConnection() => new()
    {
        Id = "conn",
        Provider = ProviderKind.Dataverse,
        Cloud = CloudInstance.Public,
        TenantId = "tenant-id",
    };

    private static Credential TestCredential() => new()
    {
        Id = "cred",
        Kind = CredentialKind.InteractiveBrowser,
    };

    private static HttpResponseMessage JsonResponse(string json)
        => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

    private sealed class FakeAccessTokenService : TALXIS.CLI.Core.Abstractions.IAccessTokenService
    {
        public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
            => Task.FromResult("token");
    }

    private sealed class FakeHttpClientFactoryWrapper : TALXIS.CLI.Core.Abstractions.IHttpClientFactoryWrapper
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers;

        public FakeHttpClientFactoryWrapper(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
            => _handlers = handlers;

        public HttpClient Create() => new(new FakeHttpMessageHandler(_handlers));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers;

        public FakeHttpMessageHandler(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
            => _handlers = handlers;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_handlers.Count == 0)
                throw new InvalidOperationException("No HTTP handler configured for this request.");

            return Task.FromResult(_handlers.Dequeue()(request));
        }
    }
}
