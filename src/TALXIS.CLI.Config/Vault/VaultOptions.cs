using TALXIS.CLI.Config.Resolution;

namespace TALXIS.CLI.Config.Vault;

/// <summary>
/// Per-cache-file vault configuration: cache filename, OS-vault identity, and
/// plaintext-fallback / file-based toggles. Two named factories exist so the
/// generic secret vault and the MSAL token cache stay independent (different
/// blast radius, different lifetimes, independent plaintext-fallback consent),
/// matching the pac CLI layout.
/// </summary>
public sealed record VaultOptions
{
    /// <summary>Keychain service name (also Linux keyring schema name).</summary>
    public const string KeychainService = "com.talxis.txc";

    /// <summary>Cache file name, relative to <see cref="Storage.ConfigPaths.AuthDirectory"/>.</summary>
    public required string CacheFileName { get; init; }

    /// <summary>macOS Keychain account; also used as Linux keyring attribute.</summary>
    public required string KeychainAccount { get; init; }

    /// <summary>Linux keyring label shown in Seahorse / gnome-keyring UIs.</summary>
    public required string LinuxKeyringLabel { get; init; }

    /// <summary>Linux keyring <c>CacheKind</c> attribute value.</summary>
    public required string LinuxCacheKind { get; init; }

    /// <summary>
    /// When true, the OS vault is bypassed and the cache is stored unencrypted
    /// to <see cref="FallbackCacheFileName"/>. Chosen explicitly via env var or
    /// flag, not as an automatic fallback. Triggers an ILogger warning on every
    /// read and write.
    /// </summary>
    public bool UsePlaintextFallback { get; init; }

    /// <summary>Human-readable reason plaintext mode was selected (env var name, flag, etc.).</summary>
    public string? PlaintextReason { get; init; }

    /// <summary>
    /// Fallback filename used when <see cref="UsePlaintextFallback"/> is true.
    /// Distinct from <see cref="CacheFileName"/> so <c>ls ~/.txc/auth/</c>
    /// makes the unprotected state visible at a glance.
    /// </summary>
    public string FallbackCacheFileName =>
        CacheFileName.EndsWith(".dat", StringComparison.Ordinal)
            ? CacheFileName[..^".dat".Length] + ".fallback.dat"
            : CacheFileName + ".fallback.dat";

    /// <summary>
    /// Options for the generic secret vault (<c>txc.secrets.v1.dat</c>): holds
    /// client-secret / PAT / certificate-password blobs keyed by
    /// <c>"{credentialId}::{slot}"</c>.
    /// </summary>
    public static VaultOptions Secrets(IEnvironmentReader env)
    {
        ArgumentNullException.ThrowIfNull(env);
        var (plaintext, reason) = ResolvePlaintextOptIn(env);
        return new VaultOptions
        {
            CacheFileName = "txc.secrets.v1.dat",
            KeychainAccount = "secrets",
            LinuxKeyringLabel = "TALXIS CLI secrets",
            LinuxCacheKind = "TXC_Secret_Vault",
            UsePlaintextFallback = plaintext,
            PlaintextReason = reason,
        };
    }

    /// <summary>
    /// Options for the MSAL token cache (<c>txc.msal.tokens.v1.dat</c>).
    /// Reserved here so naming is locked before the Dataverse provider lands
    /// in milestone 4; not yet wired into DI.
    /// </summary>
    public static VaultOptions MsalTokenCache(IEnvironmentReader env)
    {
        ArgumentNullException.ThrowIfNull(env);
        var (plaintext, reason) = ResolvePlaintextOptIn(env);
        return new VaultOptions
        {
            CacheFileName = "txc.msal.tokens.v1.dat",
            KeychainAccount = "msal-tokens",
            LinuxKeyringLabel = "TALXIS CLI MSAL token cache",
            LinuxCacheKind = "TXC_Msal_Token_Cache",
            UsePlaintextFallback = plaintext,
            PlaintextReason = reason,
        };
    }

    /// <summary>
    /// Env var names honored to pick plaintext / file-based storage. Intentionally
    /// public so the (future) `--plaintext-fallback` flag can set the same value.
    /// </summary>
    public const string LinuxPlaintextEnvVar = "TXC_PLAINTEXT_FALLBACK";
    public const string MacFileModeEnvVar = "TXC_TOKEN_CACHE_MODE";

    private static (bool plaintext, string? reason) ResolvePlaintextOptIn(IEnvironmentReader env)
    {
        if (OperatingSystem.IsLinux())
        {
            var v = env.Get(LinuxPlaintextEnvVar);
            if (IsTruthy(v))
                return (true, $"{LinuxPlaintextEnvVar}={v}");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var v = env.Get(MacFileModeEnvVar);
            if (!string.IsNullOrEmpty(v) && string.Equals(v, "file", StringComparison.OrdinalIgnoreCase))
                return (true, $"{MacFileModeEnvVar}=file");
        }
        return (false, null);
    }

    private static bool IsTruthy(string? v) =>
        !string.IsNullOrEmpty(v) &&
        (v == "1" ||
         string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase));
}
