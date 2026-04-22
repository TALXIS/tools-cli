using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client.Extensions.Msal;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Config.Storage;
using TALXIS.CLI.Config.Vault;

namespace TALXIS.CLI.Config.Providers.Dataverse.Msal;

/// <summary>
/// Holds the process-wide <see cref="MsalCacheHelper"/> for the MSAL token
/// cache file (<c>txc.msal.tokens.v1.dat</c>). Keeping this as a DI singleton
/// is critical on macOS: every new helper instantiation is an additional
/// Keychain prompt (see <c>session/files/keychain-prompt-research.md</c>).
/// </summary>
/// <remarks>
/// This binder is distinct from <see cref="Vault.MsalBackedCredentialVault"/>:
/// generic secrets (client secret / PAT / cert password) and refresh tokens
/// live in different cache files with different lifetimes, blast radiuses,
/// and plaintext-fallback consent. Same cache-helper pattern, different file.
/// </remarks>
public sealed class DataverseTokenCacheBinder
{
    private readonly MsalCacheHelper _helper;

    /// <summary>Diagnostic hook for tests.</summary>
    internal MsalCacheHelper Helper => _helper;

    public bool UsesPlaintextFallback { get; }

    private DataverseTokenCacheBinder(MsalCacheHelper helper, bool usesPlaintextFallback)
    {
        _helper = helper;
        UsesPlaintextFallback = usesPlaintextFallback;
    }

    /// <summary>
    /// Attach the shared MSAL token cache to a newly-built public- or
    /// confidential-client application. Safe to call many times across clients
    /// — the underlying helper is shared.
    /// </summary>
    public void Attach(Microsoft.Identity.Client.ITokenCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _helper.RegisterCache(cache);
    }

    public static async Task<DataverseTokenCacheBinder> CreateAsync(
        ConfigPaths paths,
        IEnvironmentReader env,
        ILogger<DataverseTokenCacheBinder>? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(env);
        logger ??= NullLogger<DataverseTokenCacheBinder>.Instance;

        var options = VaultOptions.MsalTokenCache(env);
        var helper = await MsalCacheHelperFactory.CreateAsync(options, paths, logger, ct).ConfigureAwait(false);
        return new DataverseTokenCacheBinder(helper, options.UsePlaintextFallback);
    }

    /// <summary>Test-only factory that accepts an explicit <see cref="VaultOptions"/>.</summary>
    internal static async Task<DataverseTokenCacheBinder> CreateForTestingAsync(
        VaultOptions options,
        ConfigPaths paths,
        ILogger<DataverseTokenCacheBinder>? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        logger ??= NullLogger<DataverseTokenCacheBinder>.Instance;

        var helper = await MsalCacheHelperFactory.CreateAsync(options, paths, logger, ct).ConfigureAwait(false);
        return new DataverseTokenCacheBinder(helper, options.UsePlaintextFallback);
    }
}
