using System.Net;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class MicrosoftGraphClientTests
{
    [Fact]
    public async Task ListServicePrincipalsAsync_UsesGraphAudience_AndParsesItems()
    {
        var tokens = new FakeAccessTokenService();
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            Assert.Equal("https://graph.microsoft.com/v1.0/servicePrincipals?$select=id,appId,displayName&$filter=displayName%20eq%20%27Contoso%27&$top=5", req.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "value": [
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "appId": "22222222-2222-2222-2222-222222222222",
                      "displayName": "Contoso App"
                    }
                  ]
                }
                """)
            };
        });

        var sut = new MicrosoftGraphClient(tokens, http);
        var results = await sut.ListServicePrincipalsAsync(TestConnection(), TestCredential(), "displayName eq 'Contoso'", 5, CancellationToken.None);

        Assert.Equal(new Uri("https://graph.microsoft.com/"), tokens.LastResourceUri);
        var principal = Assert.Single(results);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), principal.Id);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), principal.AppId);
        Assert.Equal("Contoso App", principal.DisplayName);
    }

    [Fact]
    public async Task ListServicePrincipalsAsync_FollowsODataNextLink_AcrossMultiplePages()
    {
        var callCount = 0;
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "value": [
                        { "id": "11111111-1111-1111-1111-111111111111", "appId": "22222222-2222-2222-2222-222222222222", "displayName": "App One" }
                      ],
                      "@odata.nextLink": "https://graph.microsoft.com/v1.0/servicePrincipals?$skiptoken=abc"
                    }
                    """)
                };
            }

            Assert.Equal("https://graph.microsoft.com/v1.0/servicePrincipals?$skiptoken=abc", req.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "value": [
                    { "id": "33333333-3333-3333-3333-333333333333", "appId": "44444444-4444-4444-4444-444444444444", "displayName": "App Two" }
                  ]
                }
                """)
            };
        });

        var sut = new MicrosoftGraphClient(new FakeAccessTokenService(), http);
        var results = await sut.ListServicePrincipalsAsync(TestConnection(), TestCredential(), filter: null, top: 1, CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.DisplayName == "App One");
        Assert.Contains(results, r => r.DisplayName == "App Two");
    }

    [Fact]
    public async Task ListUsersAsync_WithApplicationCredential403_ThrowsClearPermissionError()
    {
        var http = new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":\"forbidden\"}")
        });
        var sut = new MicrosoftGraphClient(new FakeAccessTokenService(), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ListUsersAsync(TestConnection(), TestCredential(CredentialKind.ClientSecret), null, null, CancellationToken.None));

        Assert.Contains("User.Read.All", ex.Message);
        Assert.Contains("admin-consented", ex.Message);
    }

    private static Connection TestConnection() => new()
    {
        Id = "conn",
        Provider = ProviderKind.Dataverse,
        Cloud = CloudInstance.Public,
        TenantId = "tenant-id",
    };

    private static Credential TestCredential(CredentialKind kind = CredentialKind.InteractiveBrowser) => new()
    {
        Id = "cred",
        Kind = kind,
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
