using System.Net;
using System.Net.Http;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class PowerPlatformEnvironmentProvisionerTests
{
    private static readonly Guid NewEnvId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Connection Conn() => new()
    {
        Id = "conn",
        Provider = ProviderKind.Dataverse,
        EnvironmentUrl = "https://contoso.crm.dynamics.com/",
        Cloud = CloudInstance.Public,
        TenantId = "tenant-1",
    };

    private static Credential Cred() => new()
    {
        Id = "cred",
        Kind = CredentialKind.InteractiveBrowser,
    };

    [Fact]
    public async Task CreateAsync_FireAndForget_PostsBodyAndReturnsQueuedOperation()
    {
        string? capturedBody = null;
        var operationLocation = new Uri("https://api.bap.microsoft.com/operations/op-1");

        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("environmentCurrencies"))
                return Json(HttpStatusCode.OK, CurrencyCatalog());

            if (req.Method == HttpMethod.Post)
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                var resp = Json(HttpStatusCode.Accepted, EnvironmentBody("Provisioning"));
                resp.Headers.Location = operationLocation;
                return resp;
            }

            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        var sut = new PowerPlatformEnvironmentProvisioner(new FakeTokens(), http);

        var result = await sut.CreateAsync(Conn(), Cred(), new EnvironmentCreateRequest
        {
            DisplayName = "Contoso Dev",
            EnvironmentType = EnvironmentType.Sandbox,
            Region = "unitedstates",
            CurrencyCode = "USD",
            Language = "1033",
            Wait = false,
        }, CancellationToken.None);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        var props = doc.RootElement.GetProperty("properties");
        Assert.Equal("unitedstates", doc.RootElement.GetProperty("location").GetString());
        Assert.Equal("Contoso Dev", props.GetProperty("displayName").GetString());
        Assert.Equal("Sandbox", props.GetProperty("environmentSku").GetString());
        Assert.Equal("CommonDataService", props.GetProperty("databaseType").GetString());
        Assert.Equal("USD", props.GetProperty("linkedEnvironmentMetadata").GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal(1033, props.GetProperty("linkedEnvironmentMetadata").GetProperty("baseLanguage").GetInt32());

        Assert.Equal(NewEnvId, result.EnvironmentId);
        Assert.False(result.Completed);
        Assert.Equal(operationLocation, result.OperationLocation);
    }

    [Fact]
    public async Task CreateAsync_Wait_PollsOperationUntilComplete()
    {
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("environmentCurrencies"))
                return Json(HttpStatusCode.OK, CurrencyCatalog());

            if (req.Method == HttpMethod.Post)
            {
                var resp = Json(HttpStatusCode.Accepted, EnvironmentBody("Provisioning"));
                resp.Headers.Location = new Uri("https://api.bap.microsoft.com/operations/op-1");
                return resp;
            }

            // First poll returns terminal success immediately (no Task.Delay hit).
            return Json(HttpStatusCode.OK, EnvironmentBody("Succeeded"));
        });

        var sut = new PowerPlatformEnvironmentProvisioner(new FakeTokens(), http);

        var result = await sut.CreateAsync(Conn(), Cred(), new EnvironmentCreateRequest
        {
            DisplayName = "Contoso Dev",
            EnvironmentType = EnvironmentType.Sandbox,
            Wait = true,
        }, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal("Succeeded", result.Status);
        Assert.Null(result.OperationLocation);
    }

    [Fact]
    public async Task CreateAsync_DefaultType_ThrowsArgumentException()
    {
        var sut = new PowerPlatformEnvironmentProvisioner(new FakeTokens(), Unreachable());

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateAsync(Conn(), Cred(),
            new EnvironmentCreateRequest { DisplayName = "x", EnvironmentType = EnvironmentType.Default },
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_TeamsWithoutSecurityGroup_ThrowsArgumentException()
    {
        var sut = new PowerPlatformEnvironmentProvisioner(new FakeTokens(), Unreachable());

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateAsync(Conn(), Cred(),
            new EnvironmentCreateRequest { EnvironmentType = EnvironmentType.Teams },
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_UnknownCurrency_ThrowsArgumentException()
    {
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("environmentCurrencies"))
                return Json(HttpStatusCode.OK, CurrencyCatalog());
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        var sut = new PowerPlatformEnvironmentProvisioner(new FakeTokens(), http);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateAsync(Conn(), Cred(),
            new EnvironmentCreateRequest
            {
                DisplayName = "x",
                EnvironmentType = EnvironmentType.Sandbox,
                CurrencyCode = "ZZZ",
            },
            CancellationToken.None));

        Assert.Contains("USD", ex.Message);
    }

    private static string CurrencyCatalog()
        => "{\"value\":[{\"properties\":{\"code\":\"USD\",\"localizedName\":\"US Dollar\",\"symbol\":\"$\"}}]}";

    private static string EnvironmentBody(string state)
        => $"{{\"name\":\"{NewEnvId}\",\"properties\":{{\"displayName\":\"Contoso Dev\",\"provisioningState\":\"{state}\",\"environmentSku\":\"Sandbox\"}}}}";

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body) };

    private static FakeHttpClientFactoryWrapper Unreachable()
        => new(_ => throw new InvalidOperationException("HTTP should not be called for pre-flight validation failures."));

    private sealed class FakeTokens : IAccessTokenService
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
