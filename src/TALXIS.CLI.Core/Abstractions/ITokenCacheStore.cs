namespace TALXIS.CLI.Core.Abstractions;

/// <summary>
/// Process-level token cache store used by txc providers. Exposed so generic
/// config commands can clear provider auth state without referencing provider
/// assemblies directly.
/// </summary>
public interface ITokenCacheStore
{
    void Clear();
}
