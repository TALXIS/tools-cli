using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Tests.Shared;

/// <summary>Resolves every profile request to the same context.</summary>
internal sealed class FixedConfigurationResolver(ResolvedProfileContext context) : IConfigurationResolver
{
    public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
        => Task.FromResult(context);
}

/// <summary>Issues a constant bearer token for any audience.</summary>
internal sealed class FakeAccessTokenService : IAccessTokenService
{
    public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
        => Task.FromResult("token");
}

/// <summary>An environment catalog that knows no environments.</summary>
internal sealed class EmptyEnvironmentCatalog : IPowerPlatformEnvironmentCatalog
{
    public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(Connection connection, Credential credential, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PowerPlatformEnvironmentSummary>>(Array.Empty<PowerPlatformEnvironmentSummary>());

    public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(Connection connection, Credential credential, Uri environmentUrl, CancellationToken ct)
        => Task.FromResult<PowerPlatformEnvironmentSummary?>(null);
}

/// <summary>
/// Hands out HTTP clients answered by a delegate: one handler for every request,
/// or a queue consumed one request at a time.
/// </summary>
internal sealed class FakeHttpClientFactoryWrapper : IHttpClientFactoryWrapper
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FakeHttpClientFactoryWrapper(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

    public FakeHttpClientFactoryWrapper(Queue<Func<HttpRequestMessage, HttpResponseMessage>> handlers)
        => _handler = request => handlers.Count == 0
            ? throw new InvalidOperationException("No HTTP handler configured for this request.")
            : handlers.Dequeue()(request);

    public HttpClient Create() => new(new DelegateHandler(_handler));

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
