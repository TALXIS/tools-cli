using System.Net;
using System.Net.Http;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class ConnectorCatalogServiceTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task ListConnectorsAsync_UsesPowerAppsAudience_AndEnvironmentFilter()
    {
        var tokens = new FakeAccessTokenService();
        Uri? requestedUri = null;
        var http = new FakeHttpClientFactoryWrapper(request =>
        {
            requestedUri = request.RequestUri;
            return Json("""{"value":[{"name":"shared_teams","properties":{"displayName":"Microsoft Teams","tier":"Standard"}}]}""");
        });

        var sut = CreateService(tokens, http);
        var connectors = await sut.ListConnectorsAsync(null, CancellationToken.None);

        Assert.Equal(new Uri("https://service.powerapps.com/"), tokens.LastResourceUri);
        Assert.NotNull(requestedUri);
        Assert.StartsWith("https://api.powerapps.com/providers/Microsoft.PowerApps/apis", requestedUri!.AbsoluteUri);
        Assert.Contains("api-version=2016-11-01", requestedUri.Query);
        Assert.Contains(Uri.EscapeDataString($"environment eq '{EnvironmentId:D}'"), requestedUri.Query);

        var connector = Assert.Single(connectors);
        Assert.Equal("shared_teams", connector.Name);
        Assert.Equal("Microsoft Teams", connector.DisplayName);
        Assert.Equal("Standard", connector.Tier);
        Assert.False(connector.IsCustomApi);
    }

    [Fact]
    public async Task ListConnectorsAsync_FollowsNextLink_AndSortsByName()
    {
        var page1 = """{"value":[{"name":"shared_zendesk","properties":{"displayName":"Zendesk"}}],"nextLink":"https://api.powerapps.com/providers/Microsoft.PowerApps/apis?page=2"}""";
        var page2 = """{"value":[{"name":"shared_approvals","properties":{"displayName":"Approvals","isCustomApi":true}}]}""";

        var calls = 0;
        var http = new FakeHttpClientFactoryWrapper(_ => Json(++calls == 1 ? page1 : page2));

        var sut = CreateService(new FakeAccessTokenService(), http);
        var connectors = await sut.ListConnectorsAsync(null, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(["shared_approvals", "shared_zendesk"], connectors.Select(c => c.Name).ToList());
        Assert.True(connectors[0].IsCustomApi);
    }

    [Fact]
    public async Task GetOperationAsync_ParsesInlineSwagger_AndBuildsApiId()
    {
        Uri? requestedUri = null;
        var http = new FakeHttpClientFactoryWrapper(request =>
        {
            requestedUri = request.RequestUri;
            return Json("""
            {
              "name": "shared_test",
              "properties": {
                "swagger": {
                  "paths": {
                    "/items": {
                      "post": {
                        "operationId": "CreateItem",
                        "parameters": [ { "name": "folder", "in": "query", "type": "string", "required": true } ]
                      }
                    }
                  }
                }
              }
            }
            """);
        });

        var sut = CreateService(new FakeAccessTokenService(), http);
        var detail = await sut.GetOperationAsync(null, "shared_test", "CreateItem", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Contains("$expand=properties.swagger", requestedUri!.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/providers/Microsoft.PowerApps/apis/shared_test", detail.ApiId);
        Assert.Equal("CreateItem", detail.OperationId);
        var parameter = Assert.Single(detail.Parameters);
        Assert.Equal("folder", parameter.Name);
    }

    [Fact]
    public async Task GetOperationAsync_FallsBackToApiDefinitionsBlob_WithoutAuthorizationHeader()
    {
        var blobRequests = new List<HttpRequestMessage>();
        var http = new FakeHttpClientFactoryWrapper(request =>
        {
            if (request.RequestUri!.Host == "blob.example.com")
            {
                blobRequests.Add(request);
                return Json("""{"paths":{"/x":{"get":{"operationId":"GetX"}}}}""");
            }

            return Json("""{"name":"shared_test","properties":{"apiDefinitions":{"originalSwaggerUrl":"https://blob.example.com/swagger.json?sig=abc"}}}""");
        });

        var sut = CreateService(new FakeAccessTokenService(), http);
        var operations = await sut.ListOperationsAsync(null, "shared_test", CancellationToken.None);

        var operation = Assert.Single(operations);
        Assert.Equal("GetX", operation.OperationId);
        var blobRequest = Assert.Single(blobRequests);
        Assert.Null(blobRequest.Headers.Authorization);
    }

    [Fact]
    public async Task GetSwagger_Throws_WhenConnectorHasNoDefinition()
    {
        var http = new FakeHttpClientFactoryWrapper(_ => Json("""{"name":"shared_test","properties":{}}"""));
        var sut = CreateService(new FakeAccessTokenService(), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ListOperationsAsync(null, "shared_test", CancellationToken.None));
        Assert.Contains("no OpenAPI definition", ex.Message);
    }

    [Fact]
    public async Task GetSwagger_ThrowsFriendlyError_WhenPropertiesIsNull()
    {
        var http = new FakeHttpClientFactoryWrapper(_ => Json("""{"name":"shared_test","properties":null}"""));
        var sut = CreateService(new FakeAccessTokenService(), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ListOperationsAsync(null, "shared_test", CancellationToken.None));
        Assert.Contains("no OpenAPI definition", ex.Message);
    }

    [Fact]
    public async Task GetSwagger_ThrowsNotFoundHint_On404()
    {
        var http = new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}"),
        });
        var sut = CreateService(new FakeAccessTokenService(), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ListOperationsAsync(null, "shared_missing", CancellationToken.None));
        Assert.Contains("or the environment was not found", ex.Message);
        Assert.Contains("connector list", ex.Message);
    }

    [Fact]
    public async Task ListConnectorsAsync_ResolvesRelativeNextLink_AgainstBaseUri()
    {
        var requests = new List<Uri>();
        var http = new FakeHttpClientFactoryWrapper(request =>
        {
            requests.Add(request.RequestUri!);
            return Json(requests.Count == 1
                ? """{"value":[],"nextLink":"providers/Microsoft.PowerApps/apis?page=2"}"""
                : """{"value":[]}""");
        });

        var sut = CreateService(new FakeAccessTokenService(), http);
        await sut.ListConnectorsAsync(null, CancellationToken.None);

        Assert.Equal(2, requests.Count);
        Assert.Equal("https://api.powerapps.com/providers/Microsoft.PowerApps/apis?page=2", requests[1].AbsoluteUri);
    }

    [Fact]
    public async Task ListConnectorsAsync_Throws_WithTruncatedBody_OnHttpFailure()
    {
        var http = new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("denied"),
        });

        var sut = CreateService(new FakeAccessTokenService(), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ListConnectorsAsync(null, CancellationToken.None));
        Assert.Contains("403", ex.Message);
        Assert.Contains("denied", ex.Message);
    }

    private static ConnectorCatalogService CreateService(FakeAccessTokenService tokens, FakeHttpClientFactoryWrapper http)
        => new(
            new FakeResolver(new ResolvedProfileContext(
                Profile: null,
                Connection: new Connection
                {
                    Id = "conn",
                    Provider = ProviderKind.Dataverse,
                    EnvironmentUrl = "https://contoso.crm.dynamics.com/",
                    Cloud = CloudInstance.Public,
                    EnvironmentId = EnvironmentId,
                },
                Credential: new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser },
                Source: ResolutionSource.Global)),
            new ThrowingCatalog(),
            tokens,
            http);

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeResolver : IConfigurationResolver
    {
        private readonly ResolvedProfileContext _ctx;

        public FakeResolver(ResolvedProfileContext ctx) => _ctx = ctx;

        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
            => Task.FromResult(_ctx);
    }

    // EnvironmentId is stored on the connection, so the catalog must never be hit.
    private sealed class ThrowingCatalog : IPowerPlatformEnvironmentCatalog
    {
        public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(
            Connection connection, Credential credential, CancellationToken ct)
            => throw new InvalidOperationException("Catalog should not be called.");

        public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(
            Connection connection, Credential credential, Uri environmentUrl, CancellationToken ct)
            => throw new InvalidOperationException("Catalog should not be called.");
    }

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

        public FakeHttpClientFactoryWrapper(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpClient Create() => new(new FakeHttpMessageHandler(_handler));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
