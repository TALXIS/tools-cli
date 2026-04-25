using System.Collections.Concurrent;
using Microsoft.Identity.Client;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Application-level in-memory token cache with a proactive renewal buffer.
/// Sits in front of MSAL to avoid repeated disk I/O and network round-trips
/// for the same (credential, tenant, resource) tuple within a single process.
/// </summary>
/// <remarks>
/// PAC CLI uses an equivalent <c>ExchangeTokenSilentAsync</c> layer with a
/// 1-minute buffer. We use 2 minutes to account for network latency during
/// long-running operations (PackageDeployer, CMT imports).
/// <para>
/// Thread-safety: <see cref="ConcurrentDictionary{TKey, TValue}"/> for the
/// cache entries, plus <see cref="SemaphoreSlim"/> per key to prevent
/// stampedes when multiple threads request the same token simultaneously.
/// </para>
/// </remarks>
internal sealed class InMemoryTokenCache
{
    /// <summary>
    /// Tokens expiring within this window are considered stale and will be
    /// refreshed proactively before they actually expire.
    /// </summary>
    private static readonly TimeSpan ProactiveRenewalBuffer = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a cached access token if still valid beyond the proactive
    /// renewal buffer, or <c>null</c> if the caller should acquire a fresh
    /// token from MSAL.
    /// </summary>
    public string? TryGet(string cacheKey)
    {
        if (_cache.TryGetValue(cacheKey, out var entry) &&
            entry.ExpiresOn.UtcDateTime > DateTime.UtcNow.Add(ProactiveRenewalBuffer))
        {
            return entry.AccessToken;
        }
        return null;
    }

    /// <summary>
    /// Stores or updates a cached token.
    /// </summary>
    public void Set(string cacheKey, AuthenticationResult result)
    {
        _cache[cacheKey] = new CacheEntry(result.AccessToken, result.ExpiresOn);
    }

    /// <summary>
    /// Gets or creates a per-key semaphore to prevent concurrent MSAL
    /// requests for the same token. Callers must <c>await</c> the semaphore
    /// and release it after acquiring and caching the token.
    /// </summary>
    public SemaphoreSlim GetKeyLock(string cacheKey)
    {
        return _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Builds a deterministic cache key from the credential and resource.
    /// </summary>
    public static string BuildKey(string credentialId, string? tenantId, string resourceAuthority)
    {
        return $"{credentialId}|{tenantId ?? "default"}|{resourceAuthority}";
    }

    private readonly record struct CacheEntry(string AccessToken, DateTimeOffset ExpiresOn);
}
