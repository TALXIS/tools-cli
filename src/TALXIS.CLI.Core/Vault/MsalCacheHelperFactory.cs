using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Extensions.Msal;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Core.Vault;

/// <summary>
/// Builds <see cref="MsalCacheHelper"/> instances for a given <see cref="VaultOptions"/>,
/// handling OS-vault configuration and plaintext-fallback routing. All per-OS
/// interop (DPAPI, Keychain, libsecret) is delegated to MSAL Extensions — we
/// never touch the Security framework, D-Bus, or ProtectedData directly.
/// </summary>
internal static class MsalCacheHelperFactory
{
    /// <summary>
    /// Creates and verifies an <see cref="MsalCacheHelper"/>. When the OS
    /// credential vault is unavailable (e.g. no libsecret/D-Bus on Linux,
    /// no DPAPI in a container), automatically falls back to a plaintext
    /// file-based cache with a warning — matching the pac CLI behaviour.
    /// Set <c>TXC_PLAINTEXT_FALLBACK=1</c> to skip the keyring attempt
    /// entirely and go straight to plaintext.
    /// </summary>
    public static async Task<MsalCacheHelper> CreateAsync(
        VaultOptions options,
        ConfigPaths paths,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        Directory.CreateDirectory(paths.AuthDirectory);

        if (options.UsePlaintextFallback)
            return await CreatePlaintextAsync(options, paths, logger).ConfigureAwait(false);

        var props = BuildProtectedProperties(options, paths);
        var helper = await MsalCacheHelper.CreateAsync(props).ConfigureAwait(false);
        try
        {
            helper.VerifyPersistence();
            return helper;
        }
        catch (MsalCachePersistenceException ex)
        {
            // OS vault is unavailable (missing libsecret, no D-Bus session,
            // container without DPAPI, etc.). Auto-fallback to plaintext with
            // a warning rather than hard-failing — the user can still work.
            string hint = GetPlatformHint();
            logger.LogWarning(ex,
                "OS credential vault is unavailable — falling back to plaintext file cache. " +
                "{Hint} To suppress this warning, set {EnvVar}=1 explicitly.",
                hint, VaultOptions.PlaintextFallbackEnvVar);

            // Clone options with an explicit reason so the downstream plaintext
            // warning log accurately reflects automatic fallback rather than
            // an explicit opt-in.
            var fallbackOptions = options with { UsePlaintextFallback = true, PlaintextReason = "auto-fallback (OS vault unavailable)" };
            return await CreatePlaintextAsync(fallbackOptions, paths, logger).ConfigureAwait(false);
        }
    }

    /// <summary>Returns a user-facing hint appropriate for the current OS.</summary>
    private static string GetPlatformHint()
    {
        if (OperatingSystem.IsWindows())
            return "DPAPI is not available in this environment (container or restricted account).";
        if (OperatingSystem.IsMacOS())
            return $"Keychain is not available. You can also set {VaultOptions.MacFileModeEnvVar}=file.";
        // Linux
        return "Install `libsecret-1-0` and `gnome-keyring`, or run inside a desktop session with D-Bus.";
    }

    private static async Task<MsalCacheHelper> CreatePlaintextAsync(
        VaultOptions options,
        ConfigPaths paths,
        ILogger logger)
    {
        var fallbackPath = Path.Combine(paths.AuthDirectory, options.FallbackCacheFileName);
        var permissionNote = OperatingSystem.IsWindows()
            ? "Secrets are NOT protected by the OS; rely on NTFS ACLs only."
            : "Secrets are NOT protected by the OS; rely on file permissions (chmod 600) only.";
        logger.LogWarning(
            "Vault using PLAINTEXT file-based storage at {Path} (opt-in: {Reason}). {PermissionNote}",
            fallbackPath, options.PlaintextReason ?? "explicit", permissionNote);

        // Pre-create the file and restrict permissions before MSAL registers its
        // cache callbacks. MsalCacheHelper.CreateAsync does not create the file
        // immediately — it only registers AfterAccess/BeforeAccess delegates that
        // write on demand. If we call TrySetOwnerOnlyPermissions after CreateAsync
        // the file doesn't exist yet, the chmod is skipped, and MSAL later creates
        // the file with the process umask (typically 0644, group+world readable).
        EnsureOwnerOnlyFile(fallbackPath, logger);

        var props = new StorageCreationPropertiesBuilder(options.FallbackCacheFileName, paths.AuthDirectory)
            .WithUnprotectedFile()
            .Build();

        return await MsalCacheHelper.CreateAsync(props).ConfigureAwait(false);
    }

    private static StorageCreationProperties BuildProtectedProperties(VaultOptions options, ConfigPaths paths)
    {
        var builder = new StorageCreationPropertiesBuilder(options.CacheFileName, paths.AuthDirectory)
            .WithMacKeyChain(VaultOptions.KeychainService, options.KeychainAccount)
            .WithLinuxKeyring(
                schemaName: VaultOptions.KeychainService,
                collection: MsalCacheHelper.LinuxKeyRingDefaultCollection,
                secretLabel: options.LinuxKeyringLabel,
                attribute1: new KeyValuePair<string, string>("Version", "1"),
                attribute2: new KeyValuePair<string, string>("CacheKind", options.LinuxCacheKind));
        return builder.Build();
    }

    /// <summary>
    /// Creates the plaintext cache file (if absent) and immediately restricts
    /// it to owner read/write. Must be called <em>before</em>
    /// <see cref="MsalCacheHelper.CreateAsync"/> so that MSAL's first write
    /// lands into an already-restricted file rather than creating it with the
    /// process umask (typically 0644, group+world readable).
    /// </summary>
    private static void EnsureOwnerOnlyFile(string path, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            // Open-or-create with exclusive owner permissions from the start.
            using var fs = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to pre-create or chmod 600 plaintext vault at {Path}.", path);
        }
    }
}
