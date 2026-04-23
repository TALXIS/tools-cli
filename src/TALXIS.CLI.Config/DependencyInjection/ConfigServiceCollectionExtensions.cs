using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Config.Storage;
using TALXIS.CLI.Config.Vault;

namespace TALXIS.CLI.Config.DependencyInjection;

public static class ConfigServiceCollectionExtensions
{
    /// <summary>
    /// Registers the txc config core services (stores, resolver, workspace discovery,
    /// headless detector, OS-backed credential vault).
    /// </summary>
    public static IServiceCollection AddTxcConfigCore(this IServiceCollection services)
    {
        services.AddSingleton(_ => ConfigPaths.FromEnvironment());

        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IGlobalConfigStore, GlobalConfigStore>();

        services.AddSingleton<IWorkspaceDiscovery, WorkspaceDiscovery>();
        services.AddSingleton<IEnvironmentReader>(ProcessEnvironmentReader.Instance);

        services.AddSingleton<IConfigurationResolver, ConfigurationResolver>();
        services.AddSingleton<IHeadlessDetector, HeadlessDetector>();
        services.AddSingleton<TALXIS.CLI.Config.Bootstrapping.ConnectionUpsertService>();

        // Singleton so MsalCacheHelper (and its CrossPlatLock) is instantiated
        // once per process per cache file. See `session/files/keychain-prompt-research.md`:
        // each extra instantiation is an extra Keychain prompt on macOS.
        services.AddSingleton<ICredentialVault>(sp =>
        {
            var paths = sp.GetRequiredService<ConfigPaths>();
            var env = sp.GetRequiredService<IEnvironmentReader>();
            var logger = sp.GetRequiredService<ILogger<MsalBackedCredentialVault>>();
            return MsalBackedCredentialVault
                .CreateAsync(paths, env, logger)
                .GetAwaiter().GetResult();
        });

        return services;
    }
}
