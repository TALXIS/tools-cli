using System.Net;
using System.Net.Http;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.Dataverse;
using TALXIS.CLI.Platform.Dataverse.PowerPlatform;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class PowerPlatformEnvironmentCatalogTests
{
    [Fact]
    public async Task ListAsync_UsesPowerAppsAudience_ForAdminApiToken()
    {
        var tokens = new FakeDataverseAccessTokenService();
        var http = new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":[]}")
        });

        var sut = new PowerPlatformEnvironmentCatalog(tokens, http);

        await sut.ListAsync(
            new Connection
            {
                Id = "conn",
                Provider = ProviderKind.Dataverse,
                EnvironmentUrl = "https://contoso.crm.dynamics.com/",
                Cloud = CloudInstance.Public,
            },
            new Credential
            {
                Id = "cred",
                Kind = CredentialKind.InteractiveBrowser,
            },
            CancellationToken.None);

        Assert.Equal(new Uri("https://service.powerapps.com/"), tokens.LastResourceUri);
    }

    private sealed class FakeDataverseAccessTokenService : IDataverseAccessTokenService
    {
        public Uri? LastResourceUri { get; private set; }

        public Task<string> AcquireAsync(Connection connection, Credential credential, CancellationToken ct)
            => Task.FromResult("token");

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
