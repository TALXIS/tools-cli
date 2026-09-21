using System.Net;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class PowerAutomateApiClientTests
{
    private static readonly Uri RequestUri = new("https://abc.de.environment.api.powerplatform.com/powerautomate/apis?api-version=1");

    public PowerAutomateApiClientTests()
    {
        // The learned audience is process-wide, so each test starts from a
        // clean slate rather than inheriting another test's probe result.
        PowerAutomateApiClient.ResetResolvedAudiences();
    }

    [Fact]
    public async Task SendAsync_ReturnsTheParsedBody()
    {
        var client = Build(_ => Json(HttpStatusCode.OK, """{ "value": [ { "name": "shared_teams" } ] }"""));

        using var document = await client.SendAsync(
            HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None);

        Assert.Equal("shared_teams",
            document.RootElement.GetProperty("value")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task SendAsync_TriesThePowerAppsAudienceFirst()
    {
        // Verified against a live tenant: this is the resource the
        // /powerautomate routes accept, so it must not be behind a probe.
        var audiences = new List<Uri>();
        var client = Build(_ => Json(HttpStatusCode.OK, """{ "value": [] }"""), audiences);

        using var document = await client.SendAsync(
            HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None);

        Assert.Equal(PowerAutomateEndpointProvider.PowerAppsServiceAudience, Assert.Single(audiences));
    }

    [Fact]
    public async Task SendAsync_FallsBackToTheNextAudienceWhenTheFirstIsRejected()
    {
        var audiences = new List<Uri>();
        var calls = 0;

        var client = Build(
            _ => ++calls == 1
                ? Json(HttpStatusCode.Unauthorized, "{}")
                : Json(HttpStatusCode.OK, """{ "value": [] }"""),
            audiences);

        using var document = await client.SendAsync(
            HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(
            [
                PowerAutomateEndpointProvider.PowerAppsServiceAudience,
                PowerAutomateEndpointProvider.PowerPlatformApiAudience,
            ],
            audiences);
    }

    [Fact]
    public async Task SendAsync_SkipsAnAudienceWhoseTokenCannotBeIssued()
    {
        // Entra refuses some application/resource pairs outright
        // (AADSTS65002). That must rule out the audience, not the request.
        var audiences = new List<Uri>();
        var tokens = new RecordingAccessTokenService(audiences)
        {
            FailFor = PowerAutomateEndpointProvider.PowerAppsServiceAudience,
        };

        var client = new PowerAutomateApiClient(
            tokens, new FakeHttpClientFactoryWrapper(_ => Json(HttpStatusCode.OK, """{ "value": [] }""")));

        using var document = await client.SendAsync(
            HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None);

        Assert.Equal(
            [
                PowerAutomateEndpointProvider.PowerAppsServiceAudience,
                PowerAutomateEndpointProvider.PowerPlatformApiAudience,
            ],
            audiences);
    }

    [Fact]
    public async Task SendAsync_AnAudienceOverrideReplacesTheProbe()
    {
        var audiences = new List<Uri>();
        var custom = new Uri("https://contoso.example/");
        System.Environment.SetEnvironmentVariable(PowerAutomateApiClient.AudienceEnvironmentVariable, custom.AbsoluteUri);

        try
        {
            var client = Build(_ => Json(HttpStatusCode.OK, """{ "value": [] }"""), audiences);
            using var document = await client.SendAsync(
                HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None);

            Assert.Equal(custom, Assert.Single(audiences));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(PowerAutomateApiClient.AudienceEnvironmentVariable, null);
        }
    }

    [Fact]
    public async Task SendAsync_RemembersTheWorkingAudienceForLaterCalls()
    {
        var audiences = new List<Uri>();
        var calls = 0;

        var client = Build(
            _ => ++calls == 1
                ? Json(HttpStatusCode.Forbidden, "{}")
                : Json(HttpStatusCode.OK, """{ "value": [] }"""),
            audiences);

        using (await client.SendAsync(HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None)) { }
        using (await client.SendAsync(HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None)) { }

        // Probe (rejected), probe (accepted), then straight to the known one.
        Assert.Equal(
            [
                PowerAutomateEndpointProvider.PowerAppsServiceAudience,
                PowerAutomateEndpointProvider.PowerPlatformApiAudience,
                PowerAutomateEndpointProvider.PowerPlatformApiAudience,
            ],
            audiences);
    }

    [Fact]
    public async Task SendAsync_NotFound_IsAValidationErrorSoUnknownConnectorsExitTwo()
    {
        var client = Build(_ => Json(HttpStatusCode.NotFound, """{ "error": "no such connector" }"""));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(
            HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None));

        Assert.Contains("no such connector", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ServerError_IsRetriedThenSurfaced()
    {
        var calls = 0;
        var client = Build(_ =>
        {
            calls++;
            return Json(HttpStatusCode.InternalServerError, "boom");
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(
            HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None));

        // One attempt plus two retries. A server error says nothing about the
        // audience, so the second audience is not tried.
        Assert.Equal(3, calls);
        Assert.Contains("boom", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryANonTransientFailure()
    {
        var calls = 0;
        var client = Build(_ =>
        {
            calls++;
            return Json(HttpStatusCode.BadRequest, "bad");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(
            HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SendAsync_NonJsonBody_IsReportedClearly()
    {
        var client = Build(_ => Json(HttpStatusCode.OK, "<html>sign in</html>"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(
            HttpMethod.Get, RequestUri, TestConnection(), TestCredential(), null, CancellationToken.None));

        Assert.Contains("non-JSON", ex.Message, StringComparison.Ordinal);
    }

    private static PowerAutomateApiClient Build(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        List<Uri>? audiences = null)
        => new(new RecordingAccessTokenService(audiences), new FakeHttpClientFactoryWrapper(handler));

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private static Connection TestConnection() => new()
    {
        Id = "conn",
        EnvironmentUrl = "https://example.crm4.dynamics.com/",
        EnvironmentId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Cloud = CloudInstance.Public,
    };

    private static Credential TestCredential() => new() { Id = "cred", Kind = CredentialKind.InteractiveBrowser };

    private sealed class RecordingAccessTokenService : IAccessTokenService
    {
        private readonly List<Uri>? _audiences;

        public RecordingAccessTokenService(List<Uri>? audiences) => _audiences = audiences;

        /// <summary>An audience this identity cannot be issued a token for.</summary>
        public Uri? FailFor { get; init; }

        public Task<string> AcquireForResourceAsync(
            Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
        {
            _audiences?.Add(resourceUri);

            if (FailFor is not null && FailFor == resourceUri)
                throw new InvalidOperationException($"AADSTS65002: no preauthorization for {resourceUri}.");

            return Task.FromResult($"token-for-{resourceUri.Host}");
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
