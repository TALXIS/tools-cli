using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;
using TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;

namespace TALXIS.CLI.Tests.Tenant;

/// <summary>
/// Shared HTTP-mocked test host for tenant-scope CLI command tests
/// (<c>txc tenant user</c>/<c>group</c>/<c>app</c>/<c>role</c>). Registers a
/// <see cref="MicrosoftGraphClient"/> and <see cref="TenantRoleResolver"/>
/// backed by a queue of fake HTTP responses, so tests only need to supply
/// the response bodies their command under test will request, in order.
/// </summary>
internal sealed class TenantCommandTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public TenantCommandTestHost(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfigurationResolver>(new FixedResolver(TestContext()));
        services.AddSingleton(_ => CreateGraphClient(handlers));
        services.AddSingleton(_ => CreateResolver(handlers));

        _provider = services.BuildServiceProvider();
        TxcServices.Initialize(_provider);
    }

    public void Dispose()
    {
        TxcServices.Reset();
        _provider.Dispose();
    }

    public static HttpResponseMessage JsonResponse(string json)
        => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };

    private static MicrosoftGraphClient CreateGraphClient(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
    {
        var http = new FakeHttpClientFactoryWrapper(handlers);
        var tokens = new FakeAccessTokenService();
        return new MicrosoftGraphClient(tokens, http);
    }

    private static TenantRoleResolver CreateResolver(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
    {
        var http = new FakeHttpClientFactoryWrapper(handlers);
        var tokens = new FakeAccessTokenService();
        var graph = new MicrosoftGraphClient(tokens, http);
        var rbac = new PowerPlatformRbacRoleStrategy(new PowerPlatformRbacClient(tokens, http));
        var bap = new BapAdminApplicationRoleStrategy(new BapAdminApiClient(tokens, http));
        return new TenantRoleResolver(graph, rbac, bap);
    }

    private static ResolvedProfileContext TestContext() => new(
        new Profile { Id = "test", ConnectionRef = "conn", CredentialRef = "cred" },
        new Connection
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            TenantId = "tenant-id",
            EnvironmentType = EnvironmentType.Sandbox
        },
        new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser },
        ResolutionSource.CommandLine);

    private sealed class FixedResolver(ResolvedProfileContext context) : IConfigurationResolver
    {
        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct) => Task.FromResult(context);
    }

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
            => Task.FromResult("token");
    }

    private sealed class FakeHttpClientFactoryWrapper(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers) : IHttpClientFactoryWrapper
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = handlers;

        public HttpClient Create() => new(new FakeHttpMessageHandler(_handlers));
    }

    private sealed class FakeHttpMessageHandler(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = handlers;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_handlers.Count == 0)
                throw new InvalidOperationException("No HTTP handler configured for this request.");

            return Task.FromResult(_handlers.Dequeue()(request));
        }
    }
}
