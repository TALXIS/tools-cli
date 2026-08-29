using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;
using TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class SecurityRoleResolverTests
{
    [Fact]
    public async Task AddAssignmentAsync_AdminApplication_ForUser_ThrowsValidationError()
    {
        var sut = CreateResolver(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.AddAssignmentAsync(
                TestConnection(),
                TestCredential(),
                PowerPlatformPrincipalType.User,
                "user@contoso.com",
                "admin-application",
                CancellationToken.None));

        Assert.Contains("only valid for application principals", ex.Message);
    }

    [Fact]
    public async Task AddAssignmentAsync_ApplicationRole_ResolvesGraphObjectIdAndClientId_ThenRegistersBapApplication()
    {
        HttpRequestMessage? graphRequest = null;
        HttpRequestMessage? bapPutRequest = null;

        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            req =>
            {
                graphRequest = req;
                return JsonResponse("""
                {
                  "value": [
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "appId": "22222222-2222-2222-2222-222222222222",
                      "displayName": "Contoso App"
                    }
                  ]
                }
                """);
            },
            _ => JsonResponse("[]"),
            req =>
            {
                bapPutRequest = req;
                return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent)
                {
                    Content = new StringContent(string.Empty)
                };
            }
        ]);

        var sut = CreateResolver(handlers);
        await sut.AddAssignmentAsync(
            TestConnection(),
            TestCredential(),
            PowerPlatformPrincipalType.ApplicationUser,
            "Contoso App",
            "admin-application",
            CancellationToken.None);

        Assert.NotNull(graphRequest);
        Assert.Contains("displayName%20eq%20%27Contoso%20App%27", graphRequest!.RequestUri!.AbsoluteUri);
        Assert.NotNull(bapPutRequest);
        Assert.Contains("adminApplications/22222222-2222-2222-2222-222222222222", bapPutRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task AddAssignmentAsync_RealRole_ResolvesToRbacStrategy()
    {
        string? postBody = null;
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => JsonResponse("""
            {
              "value": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "appId": "22222222-2222-2222-2222-222222222222",
                  "displayName": "Contoso App"
                }
              ]
            }
            """),
            _ => JsonResponse("""
            {
              "value": [
                {
                  "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                  "roleDefinitionName": "Tenant Reader",
                  "assignableScopes": ["/tenants/tenant-id"]
                }
              ]
            }
            """),
            _ => JsonResponse("{\"value\":[]}"),
            req =>
            {
                postBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(System.Net.HttpStatusCode.Created)
                {
                    Content = new StringContent("{}")
                };
            }
        ]);

        var sut = CreateResolver(handlers);
        await sut.AddAssignmentAsync(
            TestConnection(),
            TestCredential(),
            PowerPlatformPrincipalType.ApplicationUser,
            "22222222-2222-2222-2222-222222222222",
            "Tenant Reader",
            CancellationToken.None);

        Assert.NotNull(postBody);
        Assert.Contains("11111111-1111-1111-1111-111111111111", postBody);
        Assert.Contains("33333333-3333-3333-3333-333333333333", postBody);
    }

    [Fact]
    public async Task ListAssignmentsAsync_UserByUpn_DoesNotSendGuidTypedIdClause()
    {
        // Regression test: Microsoft Graph rejects the entire $filter with a 400 if any clause
        // compares a Guid-typed property (id) to a non-GUID value, even combined with "or" - so
        // the "id eq" clause must be omitted entirely when the identifier isn't a GUID.
        HttpRequestMessage? graphRequest = null;
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            req =>
            {
                graphRequest = req;
                return JsonResponse("""
                {
                  "value": [
                    { "id": "11111111-1111-1111-1111-111111111111", "userPrincipalName": "user@contoso.com", "displayName": "Contoso User" }
                  ]
                }
                """);
            },
            _ => JsonResponse("{\"value\":[]}"),
            _ => JsonResponse("{\"value\":[]}")
        ]);

        var sut = CreateResolver(handlers);
        await sut.ListAssignmentsAsync(
            TestConnection(),
            TestCredential(),
            PowerPlatformPrincipalType.User,
            "user@contoso.com",
            CancellationToken.None);

        Assert.NotNull(graphRequest);
        var query = Uri.UnescapeDataString(graphRequest!.RequestUri!.Query);
        Assert.DoesNotContain("id eq", query);
        Assert.Contains("userPrincipalName eq 'user@contoso.com'", query);
    }

    [Fact]
    public async Task ListAssignmentsAsync_UserByObjectId_IncludesIdClause()
    {
        HttpRequestMessage? graphRequest = null;
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            req =>
            {
                graphRequest = req;
                return JsonResponse("""
                {
                  "value": [
                    { "id": "11111111-1111-1111-1111-111111111111", "userPrincipalName": "user@contoso.com", "displayName": "Contoso User" }
                  ]
                }
                """);
            },
            _ => JsonResponse("{\"value\":[]}"),
            _ => JsonResponse("{\"value\":[]}")
        ]);

        var sut = CreateResolver(handlers);
        await sut.ListAssignmentsAsync(
            TestConnection(),
            TestCredential(),
            PowerPlatformPrincipalType.User,
            "11111111-1111-1111-1111-111111111111",
            CancellationToken.None);

        Assert.NotNull(graphRequest);
        var query = Uri.UnescapeDataString(graphRequest!.RequestUri!.Query);
        Assert.Contains("id eq '11111111-1111-1111-1111-111111111111'", query);
    }

    [Fact]
    public async Task ListAssignmentsAsync_GroupByObjectId_ResolvesWithoutAnyGraphCall()
    {
        // Groups are never resolved through Microsoft Graph (that would require the
        // "Group.Read.All" permission, which this CLI intentionally never requests -
        // see SecurityRoleResolver.ResolveGroup for the rationale). A valid GUID should
        // flow straight through to the RBAC calls with zero Graph HTTP traffic.
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => JsonResponse("{\"value\":[]}"),
            _ => JsonResponse("{\"value\":[]}")
        ]);

        var sut = CreateResolver(handlers);
        var result = await sut.ListAssignmentsAsync(
            TestConnection(),
            TestCredential(),
            PowerPlatformPrincipalType.Group,
            "11111111-1111-1111-1111-111111111111",
            CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(handlers);
    }

    [Fact]
    public async Task ListAssignmentsAsync_GroupByDisplayName_ThrowsValidationErrorWithoutAnyHttpCall()
    {
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        var sut = CreateResolver(handlers);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ListAssignmentsAsync(
                TestConnection(),
                TestCredential(),
                PowerPlatformPrincipalType.Group,
                "zzz-txc-e2e-test-group",
                CancellationToken.None));

        Assert.Contains("Entra object id", ex.Message);
        Assert.Empty(handlers);
    }


    [Fact]
    public async Task AddAssignmentAsync_ApplicationByDisplayName_DoesNotSendGuidTypedIdOrAppIdClause()
    {
        HttpRequestMessage? graphRequest = null;
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            req =>
            {
                graphRequest = req;
                return JsonResponse("""
                {
                  "value": [
                    { "id": "11111111-1111-1111-1111-111111111111", "appId": "22222222-2222-2222-2222-222222222222", "displayName": "Contoso App" }
                  ]
                }
                """);
            },
            _ => JsonResponse("[]"),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent)
            {
                Content = new StringContent(string.Empty)
            }
        ]);

        var sut = CreateResolver(handlers);
        await sut.AddAssignmentAsync(
            TestConnection(),
            TestCredential(),
            PowerPlatformPrincipalType.ApplicationUser,
            "Contoso App",
            "admin-application",
            CancellationToken.None);

        Assert.NotNull(graphRequest);
        var query = Uri.UnescapeDataString(graphRequest!.RequestUri!.Query);
        Assert.DoesNotContain("id eq", query);
        Assert.DoesNotContain("appId eq", query);
        Assert.Contains("displayName eq 'Contoso App'", query);
    }

    [Fact]
    public async Task GetTenantRoleAsync_AmbiguousName_ThrowsDistinctException()
    {
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => JsonResponse("""
            {
              "value": [
                {
                  "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                  "roleDefinitionName": "Tenant Reader",
                  "assignableScopes": ["/tenants/tenant-id"]
                },
                {
                  "roleDefinitionId": "44444444-4444-4444-4444-444444444444",
                  "roleDefinitionName": "Tenant Reader",
                  "assignableScopes": ["/tenants/tenant-id"]
                }
              ]
            }
            """)
        ]);

        var sut = CreateResolver(handlers);
        await Assert.ThrowsAsync<TenantRoleAmbiguousException>(() =>
            sut.GetTenantRoleAsync(TestConnection(), TestCredential(), "Tenant Reader", CancellationToken.None));
    }

    private static SecurityRoleResolver CreateResolver(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
    {
        var http = new FakeHttpClientFactoryWrapper(handlers);
        var tokens = new FakeAccessTokenService();
        var graph = new MicrosoftGraphClient(tokens, http);
        var rbac = new PowerPlatformRbacRoleStrategy(new PowerPlatformRbacClient(tokens, http));
        var bap = new BapAdminApplicationRoleStrategy(new BapAdminApiClient(tokens, http));
        return new SecurityRoleResolver(graph, rbac, bap);
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
