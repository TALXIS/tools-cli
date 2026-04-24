using System.Net;
using System.Net.Http.Headers;
using TALXIS.CLI.Platform.Dataverse.Runtime.Authority;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class AuthorityChallengeResolverTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    [Fact]
    public async Task GetAuthorityAsync_ParsesAuthorizationUri_FromChallenge()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(
            "Bearer",
            "authorization_uri=\"https://login.microsoftonline.com/contoso.onmicrosoft.com\", resource_id=\"https://contoso.crm.dynamics.com\""));

        using var http = new HttpClient(new StubHandler(_ => response));
        var resolver = new AuthorityChallengeResolver(http);

        var authority = await resolver.GetAuthorityAsync(new Uri("https://contoso.crm.dynamics.com/"), default);
        Assert.Equal("https://login.microsoftonline.com/contoso.onmicrosoft.com", authority.AbsoluteUri);
    }

    [Fact]
    public async Task GetAuthorityAsync_Throws_WhenStatusNot401()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var resolver = new AuthorityChallengeResolver(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.GetAuthorityAsync(new Uri("https://contoso.crm.dynamics.com/"), default));
    }

    [Fact]
    public async Task GetAuthorityAsync_Throws_WhenChallengeMissingAuthorizationUri()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", "realm=\"dataverse\""));
        using var http = new HttpClient(new StubHandler(_ => response));
        var resolver = new AuthorityChallengeResolver(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.GetAuthorityAsync(new Uri("https://contoso.crm.dynamics.com/"), default));
    }

    [Fact]
    public void TryParseAuthorizationUri_AcceptsUnquotedValue()
    {
        var header = new AuthenticationHeaderValue(
            "Bearer",
            "authorization_uri=https://login.microsoftonline.com/tenant, extra=1");
        Assert.True(AuthorityChallengeResolver.TryParseAuthorizationUri(header, out var uri));
        Assert.Equal("https://login.microsoftonline.com/tenant", uri.AbsoluteUri);
    }

    [Fact]
    public void TryParseAuthorizationUri_RejectsNonBearerScheme()
    {
        var header = new AuthenticationHeaderValue(
            "Basic",
            "authorization_uri=\"https://login.microsoftonline.com/tenant\"");
        Assert.False(AuthorityChallengeResolver.TryParseAuthorizationUri(header, out _));
    }
}
