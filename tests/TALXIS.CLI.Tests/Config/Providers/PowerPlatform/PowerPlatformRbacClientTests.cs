using System.Net;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class PowerPlatformRbacClientTests
{
    [Fact]
    public async Task ListRoleDefinitionsAsync_UsesPowerPlatformAudience_AndParsesDefinitions()
    {
        var tokens = new FakeAccessTokenService();
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            Assert.Equal("https://api.powerplatform.com/authorization/roleDefinitions?api-version=2024-10-01", req.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "value": [
                    {
                      "roleDefinitionId": "11111111-1111-1111-1111-111111111111",
                      "roleDefinitionName": "Tenant Administrator",
                      "description": "Can administer the tenant",
                      "assignableScopes": ["/tenants/tenant-id"]
                    }
                  ]
                }
                """)
            };
        });

        var sut = new PowerPlatformRbacClient(tokens, http);
        var roles = await sut.ListRoleDefinitionsAsync(TestConnection(), TestCredential(), CancellationToken.None);

        Assert.Equal(new Uri("https://api.powerplatform.com/"), tokens.LastResourceUri);
        var role = Assert.Single(roles);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), role.RoleDefinitionId);
        Assert.Equal("Tenant Administrator", role.RoleDefinitionName);
    }

    [Fact]
    public async Task ListRoleDefinitionsAsync_FollowsODataNextLink_AcrossMultiplePages()
    {
        var callCount = 0;
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            callCount++;
            if (callCount == 1)
            {
                Assert.Equal("https://api.powerplatform.com/authorization/roleDefinitions?api-version=2024-10-01", req.RequestUri!.AbsoluteUri);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "value": [
                        { "roleDefinitionId": "11111111-1111-1111-1111-111111111111", "roleDefinitionName": "Role One" }
                      ],
                      "@odata.nextLink": "https://api.powerplatform.com/authorization/roleDefinitions?api-version=2024-10-01&$skiptoken=abc"
                    }
                    """)
                };
            }

            Assert.Equal("https://api.powerplatform.com/authorization/roleDefinitions?api-version=2024-10-01&$skiptoken=abc", req.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "value": [
                    { "roleDefinitionId": "22222222-2222-2222-2222-222222222222", "roleDefinitionName": "Role Two" }
                  ]
                }
                """)
            };
        });

        var sut = new PowerPlatformRbacClient(new FakeAccessTokenService(), http);
        var roles = await sut.ListRoleDefinitionsAsync(TestConnection(), TestCredential(), CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.RoleDefinitionName == "Role One");
        Assert.Contains(roles, r => r.RoleDefinitionName == "Role Two");
    }

    [Fact]
    public async Task AddTenantRoleAssignmentAsync_SendsTenantScopeBody()
    {
        HttpMethod? capturedMethod = null;
        string? capturedBody = null;
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            capturedMethod = req.Method;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{}")
            };
        });

        var sut = new PowerPlatformRbacClient(new FakeAccessTokenService(), http);
        await sut.AddTenantRoleAssignmentAsync(
            TestConnection(),
            TestCredential(),
            TALXIS.CLI.Core.Contracts.PowerPlatform.PowerPlatformPrincipalType.ApplicationUser,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"scope\":\"/tenants/tenant-id\"", capturedBody);
        Assert.Contains("\"principalType\":\"ApplicationUser\"", capturedBody);
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

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public Uri? LastResourceUri { get; private set; }

        public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
        {
            LastResourceUri = resourceUri;
            return Task.FromResult("token");
        }
    }

    private sealed class FakeHttpClientFactoryWrapper : IHttpClientFactoryWrapper
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpClientFactoryWrapper(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        public HttpClient Create() => new(new FakeHttpMessageHandler(_handler));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
