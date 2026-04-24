using TALXIS.CLI.Core.Abstractions;

namespace TALXIS.CLI.Core.Storage;

/// <summary>
/// Default no-op token cache store used when no provider contributes a
/// persistent token cache.
/// </summary>
public sealed class NullTokenCacheStore : ITokenCacheStore
{
    public void Clear()
    {
    }
}
