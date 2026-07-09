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
    public async Task ListAdminApplicationsAsync_FollowsODataNextLink_AcrossMultiplePages()
    {
        var callCount = 0;
        var sut = new BapAdminApiClient(
            new FakeAccessTokenService(),
            new FakeHttpClientFactoryWrapper(req =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"value\":[{\"applicationId\":\"11111111-1111-1111-1111-111111111111\"}]," +
                            "\"@odata.nextLink\":\"https://example.powerapps.com/next?skiptoken=abc\"}")
                    };
                }

                Assert.Equal("https://example.powerapps.com/next?skiptoken=abc", req.RequestUri!.AbsoluteUri);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[{\"applicationId\":\"22222222-2222-2222-2222-222222222222\"}]}")
                };
            }));

        var results = await sut.ListAdminApplicationsAsync(TestConnection(), TestCredential(), CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.ApplicationId == Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.Contains(results, r => r.ApplicationId == Guid.Parse("22222222-2222-2222-2222-222222222222"));
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

    [Fact]
    public async Task AddUserToEnvironmentAsync_PostsToAddUserEndpoint_WithObjectIdBody()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var sut = new BapAdminApiClient(
            new FakeAccessTokenService(),
            new FakeHttpClientFactoryWrapper(req =>
            {
                captured = req;
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(string.Empty)
                };
            }));

        var environmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var userAadObjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        await sut.AddUserToEnvironmentAsync(TestConnection(), TestCredential(), environmentId, userAadObjectId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains(
            $"scopes/admin/environments/{environmentId}/addUser?api-version=2021-04-01",
            captured.RequestUri!.AbsoluteUri);
        Assert.NotNull(capturedBody);
        Assert.Contains(userAadObjectId.ToString(), capturedBody);
        Assert.Contains("ObjectId", capturedBody);
    }

    [Fact]
    public async Task AddUserToEnvironmentAsync_ThrowsOnFailureResponse()
    {
        var sut = new BapAdminApiClient(
            new FakeAccessTokenService(),
            new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":\"forbidden\"}")
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.AddUserToEnvironmentAsync(
            TestConnection(),
            TestCredential(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None));
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
