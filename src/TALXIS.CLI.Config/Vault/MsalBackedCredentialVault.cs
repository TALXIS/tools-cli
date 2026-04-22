using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client.Extensions.Msal;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Config.Storage;

namespace TALXIS.CLI.Config.Vault;

/// <summary>
/// <see cref="ICredentialVault"/> backed by <see cref="MsalCacheHelper"/>. Stores
/// the full vault as a single JSON dictionary blob keyed by
/// <c>"{credentialId}::{slot}"</c>. Per-OS encryption is delegated to MSAL
/// Extensions (DPAPI on Windows, Keychain on macOS, libsecret on Linux); we
/// never call those APIs directly.
/// </summary>
/// <remarks>
/// Concurrency: MSAL Extensions' <c>CrossPlatLock</c> handles cross-process
/// serialization via a <c>.lockfile</c> next to the cache. A single
/// <see cref="SemaphoreSlim"/> handles in-process callers. Do not layer more
/// locks — pac CLI layered three and it slowed things down with no gain.
/// </remarks>
public sealed class MsalBackedCredentialVault : ICredentialVault
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly MsalCacheHelper _helper;
    private readonly ILogger<MsalBackedCredentialVault> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Diagnostic hook: the underlying cache helper. Used by tests to
    /// assert that the helper is instantiated once per vault per process.</summary>
    internal MsalCacheHelper CacheHelper => _helper;

    /// <summary>Diagnostic hook: whether plaintext storage was selected.</summary>
    internal bool UsesPlaintextFallback { get; }

    private MsalBackedCredentialVault(
        MsalCacheHelper helper,
        ILogger<MsalBackedCredentialVault> logger,
        bool usesPlaintextFallback)
    {
        _helper = helper;
        _logger = logger;
        UsesPlaintextFallback = usesPlaintextFallback;
    }

    /// <summary>
    /// Factory used by DI. Builds the generic secret vault
    /// (<c>txc.secrets.v1.dat</c>) only; the MSAL token cache comes online in
    /// milestone 4.
    /// </summary>
    public static async Task<MsalBackedCredentialVault> CreateAsync(
        ConfigPaths paths,
        IEnvironmentReader env,
        ILogger<MsalBackedCredentialVault> logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(env);
        logger ??= NullLogger<MsalBackedCredentialVault>.Instance;

        var options = VaultOptions.Secrets(env);
        var helper = await MsalCacheHelperFactory.CreateAsync(options, paths, logger, ct).ConfigureAwait(false);
        return new MsalBackedCredentialVault(helper, logger, options.UsePlaintextFallback);
    }

    /// <summary>
    /// Test-only factory: builds a vault around a caller-provided
    /// <see cref="VaultOptions"/>. Enables deterministic tests that force the
    /// plaintext-file path without mutating process env vars.
    /// </summary>
    internal static async Task<MsalBackedCredentialVault> CreateForTestingAsync(
        VaultOptions options,
        ConfigPaths paths,
        ILogger<MsalBackedCredentialVault>? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        logger ??= NullLogger<MsalBackedCredentialVault>.Instance;
        var helper = await MsalCacheHelperFactory.CreateAsync(options, paths, logger, ct).ConfigureAwait(false);
        return new MsalBackedCredentialVault(helper, logger, options.UsePlaintextFallback);
    }

    public async Task<string?> GetSecretAsync(SecretRef reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var key = MakeKey(reference);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = LoadBlob();
            return map.TryGetValue(key, out var value) ? value : null;
        }
        finally { _gate.Release(); }
    }

    public async Task SetSecretAsync(SecretRef reference, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(value);
        var key = MakeKey(reference);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = LoadBlob();
            map[key] = value;
            SaveBlob(map);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteSecretAsync(SecretRef reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var key = MakeKey(reference);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var map = LoadBlob();
            if (!map.Remove(key))
                return false;
            SaveBlob(map);
            return true;
        }
        finally { _gate.Release(); }
    }

    private Dictionary<string, string> LoadBlob()
    {
        byte[] raw;
        try
        {
            raw = _helper.LoadUnencryptedTokenCache();
        }
        catch (Exception ex)
        {
            throw new VaultUnavailableException(ex);
        }

        if (raw is null || raw.Length == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw, SerializerOptions);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Vault blob failed to parse as JSON dictionary; treating as empty.");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void SaveBlob(Dictionary<string, string> map)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(map, SerializerOptions);
        try
        {
            _helper.SaveUnencryptedTokenCache(json);
        }
        catch (Exception ex)
        {
            throw new VaultUnavailableException(ex);
        }
    }

    private static string MakeKey(SecretRef r)
    {
        if (string.IsNullOrWhiteSpace(r.CredentialId))
            throw new ArgumentException("SecretRef.CredentialId must not be empty.", nameof(r));
        if (string.IsNullOrWhiteSpace(r.Slot))
            throw new ArgumentException("SecretRef.Slot must not be empty.", nameof(r));
        return $"{r.CredentialId}::{r.Slot}";
    }
}
