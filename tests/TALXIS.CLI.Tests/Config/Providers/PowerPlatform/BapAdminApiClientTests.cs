using System.Net;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class BapAdminApiClientTests
{
    [Fact]
    public async Task ListAdminApplicationsAsync_ParsesRegistrations()
    {
        var sut = new BapAdminApiClient(
            new FakeAccessTokenService(),
            new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                // Real endpoint response is OData-shaped: { "value": [...] }.
                Content = new StringContent("{\"value\":[{\"applicationId\":\"11111111-1111-1111-1111-111111111111\"}]}")
            }));

        var results = await sut.ListAdminApplicationsAsync(TestConnection(), TestCredential(), CancellationToken.None);

        var registration = Assert.Single(results);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), registration.ApplicationId);
    }

    [Fact]
    public async Task ListAdminApplicationsAsync_AcceptsBareArrayPayload()
    {
        var sut = new BapAdminApiClient(
            new FakeAccessTokenService(),
            new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"applicationId\":\"11111111-1111-1111-1111-111111111111\"}]")
            }));

        var results = await sut.ListAdminApplicationsAsync(TestConnection(), TestCredential(), CancellationToken.None);

        var registration = Assert.Single(results);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), registration.ApplicationId);
    }

    [Fact]
    public async Task RegisterAdminApplicationAsync_UsesPutEndpoint()
    {
        HttpRequestMessage? captured = null;
        var sut = new BapAdminApiClient(
            new FakeAccessTokenService(),
            new FakeHttpClientFactoryWrapper(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.NoContent)
                {
                    Content = new StringContent(string.Empty)
                };
            }));

        await sut.RegisterAdminApplicationAsync(
            TestConnection(),
            TestCredential(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.Contains("adminApplications/11111111-1111-1111-1111-111111111111?api-version=2021-04-01", captured.RequestUri!.AbsoluteUri);
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
        public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
            => Task.FromResult("token");
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
