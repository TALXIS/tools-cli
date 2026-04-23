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
    /// Creates and verifies an <see cref="MsalCacheHelper"/>. On
    /// <see cref="MsalCachePersistenceException"/>:
    /// <list type="bullet">
    ///   <item>Windows → hard error (DPAPI should always be available).</item>
    ///   <item>Linux with <c>TXC_PLAINTEXT_FALLBACK=1</c> or <paramref name="options"/>.UsePlaintextFallback → plaintext file fallback with warning.</item>
    ///   <item>Otherwise → throw <see cref="VaultUnavailableException"/>.</item>
    /// </list>
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
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                throw new VaultUnavailableException(ex);

            logger.LogWarning(ex,
                "OS credential vault (libsecret) is unavailable; no plaintext opt-in set. " +
                "Set {EnvVar}=1 to use a plaintext file fallback at chmod 600.",
                VaultOptions.LinuxPlaintextEnvVar);
            throw new VaultUnavailableException(ex);
        }
    }

    private static async Task<MsalCacheHelper> CreatePlaintextAsync(
        VaultOptions options,
        ConfigPaths paths,
        ILogger logger)
    {
        var fallbackPath = Path.Combine(paths.AuthDirectory, options.FallbackCacheFileName);
        logger.LogWarning(
            "Vault using PLAINTEXT file-based storage at {Path} (opt-in: {Reason}). " +
            "Secrets are NOT protected by the OS; rely on POSIX file permissions (chmod 600) only.",
            fallbackPath, options.PlaintextReason ?? "explicit");

        var props = new StorageCreationPropertiesBuilder(options.FallbackCacheFileName, paths.AuthDirectory)
            .WithUnprotectedFile()
            .Build();

        var helper = await MsalCacheHelper.CreateAsync(props).ConfigureAwait(false);
        TrySetOwnerOnlyPermissions(fallbackPath, logger);
        return helper;
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

    private static void TrySetOwnerOnlyPermissions(string path, ILogger logger)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
            return;

        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to chmod 600 plaintext vault at {Path}.", path);
        }
    }
}
