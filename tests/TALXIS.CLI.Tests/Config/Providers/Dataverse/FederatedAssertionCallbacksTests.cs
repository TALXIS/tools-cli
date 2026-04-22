using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TALXIS.CLI.Config.Providers.Dataverse.Msal;
using TALXIS.CLI.Config.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class FederatedAssertionCallbacksTests
{
    private sealed class DictEnv(Dictionary<string, string?> map) : IEnvironmentReader
    {
        public string? Get(string name) => map.TryGetValue(name, out var v) ? v : null;
        public string GetCurrentDirectory() => Directory.GetCurrentDirectory();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task ForAzureDevOps_PostsWithBearer_AndReturnsOidcToken()
    {
        var env = new DictEnv(new()
        {
            [FederatedAssertionCallbacks.AdoRequestUrlVar]   = "https://ado.example/oidc",
            [FederatedAssertionCallbacks.AdoRequestTokenVar] = "sys-access",
        });
        var handler = new StubHandler(_ => Json("{\"oidcToken\":\"jwt-abc\"}"));
        using var http = new HttpClient(handler);

        var jwt = await FederatedAssertionCallbacks.ForAzureDevOps(env, http)(default);

        Assert.Equal("jwt-abc", jwt);
        Assert.Equal(HttpMethod.Post, handler.Last!.Method);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "sys-access"), handler.Last.Headers.Authorization);
    }

    [Fact]
    public async Task ForAzureDevOps_AcceptsLegacyPacEnvNames()
    {
        var env = new DictEnv(new()
        {
            [FederatedAssertionCallbacks.AdoRequestUrlVarLegacy]   = "https://ado.example/oidc",
            [FederatedAssertionCallbacks.AdoRequestTokenVarLegacy] = "sys-access",
        });
        using var http = new HttpClient(new StubHandler(_ => Json("{\"oidcToken\":\"pac-jwt\"}")));
        var jwt = await FederatedAssertionCallbacks.ForAzureDevOps(env, http)(default);
        Assert.Equal("pac-jwt", jwt);
    }

    [Fact]
    public async Task ForGitHubActions_AppendsAudienceQuery_AndReturnsValue()
    {
        var env = new DictEnv(new()
        {
            [FederatedAssertionCallbacks.GitHubRequestUrlVar]   = "https://gh.example/oidc?arg=1",
            [FederatedAssertionCallbacks.GitHubRequestTokenVar] = "gh-bearer",
        });
        var handler = new StubHandler(_ => Json("{\"value\":\"gh-jwt\",\"count\":100}"));
        using var http = new HttpClient(handler);

        var jwt = await FederatedAssertionCallbacks.ForGitHubActions(env, http)(default);

        Assert.Equal("gh-jwt", jwt);
        Assert.Equal(HttpMethod.Get, handler.Last!.Method);
        Assert.Contains("audience=api%3A%2F%2FAzureADTokenExchange", handler.Last.RequestUri!.Query);
        Assert.StartsWith("?arg=1&audience=", handler.Last.RequestUri.Query);
    }

    [Fact]
    public async Task ForFederatedTokenFile_ReadsJwtFromFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"txc-fed-{Guid.NewGuid():N}.jwt");
        await File.WriteAllTextAsync(path, "  file-jwt\n");
        try
        {
            var env = new DictEnv(new() { [FederatedAssertionCallbacks.FederatedTokenFileVar] = path });
            var jwt = await FederatedAssertionCallbacks.ForFederatedTokenFile(env)(default);
            Assert.Equal("file-jwt", jwt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AutoSelect_PrefersAdo_OverGitHub_OverFile()
    {
        var env = new DictEnv(new()
        {
            [FederatedAssertionCallbacks.AdoRequestUrlVar]     = "https://ado.example/oidc",
            [FederatedAssertionCallbacks.AdoRequestTokenVar]   = "x",
            [FederatedAssertionCallbacks.GitHubRequestUrlVar]  = "https://gh.example/oidc",
            [FederatedAssertionCallbacks.GitHubRequestTokenVar] = "y",
            [FederatedAssertionCallbacks.FederatedTokenFileVar] = "/tmp/x",
        });
        var cb = FederatedAssertionCallbacks.AutoSelect(env);
        Assert.NotNull(cb);
    }

    [Fact]
    public void AutoSelect_Throws_WhenNoSourceConfigured()
    {
        var env = new DictEnv(new());
        Assert.Throws<InvalidOperationException>(() => FederatedAssertionCallbacks.AutoSelect(env));
    }
}
