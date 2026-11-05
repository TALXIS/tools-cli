using System.Collections.Concurrent;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>Tuning of <see cref="ConnectorCatalogClient"/>.</summary>
/// <param name="Lifetime">How long a fetched catalog is reused.</param>
internal sealed record ConnectorCatalogOptions(TimeSpan Lifetime)
{
    public static ConnectorCatalogOptions Default { get; } = new(TimeSpan.FromMinutes(5));
}

/// <summary>
/// The connector catalog of an environment, fetched from the connectivity API
/// and cached per environment for <see cref="ConnectorCatalogOptions.Lifetime"/>.
/// </summary>
internal sealed class ConnectorCatalogClient
{
    private readonly ConnectivityApiClient _client;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly ConcurrentDictionary<Guid, CacheEntry> _cache = new();

    public ConnectorCatalogClient(
        ConnectivityApiClient client,
        TimeProvider clock,
        ConnectorCatalogOptions? options = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _lifetime = (options ?? ConnectorCatalogOptions.Default).Lifetime;
    }

    public async Task<IReadOnlyList<CatalogConnector>> GetAsync(ConnectivityTarget target, CancellationToken ct)
    {
        if (_cache.TryGetValue(target.EnvironmentId, out var cached) && cached.ExpiresAt > _clock.GetUtcNow())
            return cached.Connectors;

        using var response = await _client.SendAsync(
            HttpMethod.Get, ConnectivityEndpoints.Connectors(target.BaseUri, target.EnvironmentId), target, null, ct)
            .ConfigureAwait(false);

        var connectors = response.Body is { } doc ? ConnectorCatalogParser.Parse(doc.RootElement) : [];
        _cache[target.EnvironmentId] = new CacheEntry(connectors, _clock.GetUtcNow() + _lifetime);
        return connectors;
    }

    private sealed record CacheEntry(IReadOnlyList<CatalogConnector> Connectors, DateTimeOffset ExpiresAt);
}
