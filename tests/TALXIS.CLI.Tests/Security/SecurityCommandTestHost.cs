using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;
using TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;

namespace TALXIS.CLI.Tests.Security;

internal sealed class SecurityCommandTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public SecurityCommandTestHost(
        Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers,
        ResolvedProfileContext? context = null,
        Action<IServiceCollection>? configureServices = null,
        FakePowerPlatformEnvironmentCatalog? environmentCatalog = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfigurationResolver>(new FixedResolver(context ?? TestContext()));
        services.AddSingleton(_ => CreateGraphClient(handlers));
        services.AddSingleton(_ => CreateResolver(handlers));
        services.AddSingleton<IPowerPlatformEnvironmentCatalog>(environmentCatalog ?? new FakePowerPlatformEnvironmentCatalog());
        configureServices?.Invoke(services);

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

    public static ResolvedProfileContext TestContext(bool includeEnvironment = false, Guid? environmentId = null) => new(
        new Profile { Id = "test", ConnectionRef = "conn", CredentialRef = "cred" },
        new Connection
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            TenantId = "tenant-id",
            EnvironmentType = EnvironmentType.Sandbox,
            EnvironmentId = includeEnvironment ? environmentId ?? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") : null,
            EnvironmentUrl = includeEnvironment ? "https://contoso.crm.dynamics.com/" : null,
            DisplayName = includeEnvironment ? "Contoso Sandbox" : null,
        },
        new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser },
        ResolutionSource.CommandLine);

    private static MicrosoftGraphClient CreateGraphClient(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
    {
        var http = new FakeHttpClientFactoryWrapper(handlers);
        var tokens = new FakeAccessTokenService();
        return new MicrosoftGraphClient(tokens, http);
    }

    private static SecurityRoleResolver CreateResolver(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
    {
        var http = new FakeHttpClientFactoryWrapper(handlers);
        var tokens = new FakeAccessTokenService();
        var graph = new MicrosoftGraphClient(tokens, http);
        var rbac = new PowerPlatformRbacRoleStrategy(new PowerPlatformRbacClient(tokens, http));
        var bap = new BapAdminApplicationRoleStrategy(new BapAdminApiClient(tokens, http));
        return new SecurityRoleResolver(graph, rbac, bap);
    }

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

    internal sealed class FakePowerPlatformEnvironmentCatalog : IPowerPlatformEnvironmentCatalog
    {
        private readonly Dictionary<Guid, PowerPlatformEnvironmentSummary> _environments = new();

        public void Add(PowerPlatformEnvironmentSummary environment)
            => _environments[environment.EnvironmentId] = environment;

        public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(Connection connection, Credential credential, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PowerPlatformEnvironmentSummary>>(_environments.Values.ToList());

        public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(Connection connection, Credential credential, Uri environmentUrl, CancellationToken ct)
            => Task.FromResult(_environments.Values.SingleOrDefault(e => e.EnvironmentUrl.AbsoluteUri == environmentUrl.AbsoluteUri));
    }
}
