using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client.Extensions.Msal;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Core.Vault;

namespace TALXIS.CLI.Core.Identity;

/// <summary>
/// Holds the process-wide <see cref="MsalCacheHelper"/> instances for the MSAL
/// token cache files. Two separate caches are maintained:
/// <list type="bullet">
///   <item><c>txc.msal.tokens.v1.dat</c> — user (public client) tokens with refresh tokens</item>
///   <item><c>txc.msal.spn-tokens.v1.dat</c> — confidential client (SPN) app tokens</item>
/// </list>
/// Keeping this as a DI singleton is critical on macOS: every new helper
/// instantiation is an additional Keychain prompt (see
/// <c>session/files/keychain-prompt-research.md</c>).
/// </summary>
/// <remarks>
/// This binder is distinct from <see cref="Vault.MsalBackedCredentialVault"/>:
/// generic secrets (client secret / PAT / cert password) and refresh tokens
/// live in different cache files with different lifetimes, blast radiuses,
/// and plaintext-fallback consent. Same cache-helper pattern, different file.
/// </remarks>
public sealed class MsalTokenCacheBinder : ITokenCacheStore
{
    private readonly MsalCacheHelper _helper;
    private readonly MsalCacheHelper? _spnHelper;

    /// <summary>Diagnostic hook for tests.</summary>
    internal MsalCacheHelper Helper => _helper;

    public bool UsesPlaintextFallback { get; }

    private MsalTokenCacheBinder(MsalCacheHelper helper, MsalCacheHelper? spnHelper, bool usesPlaintextFallback)
    {
        _helper = helper;
        _spnHelper = spnHelper;
        UsesPlaintextFallback = usesPlaintextFallback;
    }

    /// <summary>
    /// Attach the shared MSAL user token cache to a newly-built public-client
    /// application. Safe to call many times across clients — the underlying
    /// helper is shared.
    /// </summary>
    public void Attach(Microsoft.Identity.Client.ITokenCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _helper.RegisterCache(cache);
    }

    /// <summary>
    /// Attach the shared MSAL app token cache to a newly-built confidential-client
    /// application. Uses a separate cache file (<c>txc.msal.spn-tokens.v1.dat</c>)
    /// so SPN tokens persist across CLI invocations independently of user tokens.
    /// Falls back to the user cache helper if the SPN cache was not initialized.
    /// </summary>
    public void AttachAppCache(Microsoft.Identity.Client.ITokenCache appTokenCache)
    {
        ArgumentNullException.ThrowIfNull(appTokenCache);
        var helper = _spnHelper ?? _helper;
        helper.RegisterCache(appTokenCache);
    }

    /// <summary>
    /// Clears the persisted MSAL token cache files for txc.
    /// </summary>
    public void Clear()
    {
        _helper.Clear();
        _spnHelper?.Clear();
    }

    public static async Task<MsalTokenCacheBinder> CreateAsync(
        ConfigPaths paths,
        IEnvironmentReader env,
        ILogger<MsalTokenCacheBinder>? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(env);
        logger ??= NullLogger<MsalTokenCacheBinder>.Instance;

        var options = VaultOptions.MsalTokenCache(env);
        var helper = await MsalCacheHelperFactory.CreateAsync(options, paths, logger, ct).ConfigureAwait(false);

        // SPN cache is best-effort — if it fails (e.g. Keychain issues),
        // fall back to the user cache helper for app tokens too.
        MsalCacheHelper? spnHelper = null;
        try
        {
            var spnOptions = VaultOptions.MsalSpnTokenCache(env);
            spnHelper = await MsalCacheHelperFactory.CreateAsync(spnOptions, paths, logger, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create SPN token cache; confidential-client tokens will share the user cache file.");
        }

        return new MsalTokenCacheBinder(helper, spnHelper, options.UsePlaintextFallback);
    }

    /// <summary>Test-only factory that accepts an explicit <see cref="VaultOptions"/>.</summary>
    internal static async Task<MsalTokenCacheBinder> CreateForTestingAsync(
        VaultOptions options,
        ConfigPaths paths,
        ILogger<MsalTokenCacheBinder>? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        logger ??= NullLogger<MsalTokenCacheBinder>.Instance;

        var helper = await MsalCacheHelperFactory.CreateAsync(options, paths, logger, ct).ConfigureAwait(false);
        return new MsalTokenCacheBinder(helper, spnHelper: null, options.UsePlaintextFallback);
    }
}
