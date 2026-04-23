using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Platforms.Dataverse;
using TALXIS.CLI.Config.Providers.Dataverse.Authority;
using TALXIS.CLI.Config.Providers.Dataverse.Runtime;
using TALXIS.CLI.Config.Providers.Dataverse.Msal;
using TALXIS.CLI.Config.Providers.Dataverse.Services;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Config.Storage;

namespace TALXIS.CLI.Config.Providers.Dataverse.DependencyInjection;

public static class DataverseProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Dataverse <see cref="IConnectionProvider"/>, the shared
    /// MSAL client factory, the authority-challenge resolver, and the MSAL
    /// token-cache binder. Must be called after
    /// <c>AddTxcConfigCore()</c>.
    /// </summary>
    public static IServiceCollection AddTxcDataverseProvider(this IServiceCollection services)
    {
        services.AddSingleton<DataverseMsalClientFactory>();
        services.AddSingleton<AuthorityChallengeResolver>();

        // Singleton so MsalCacheHelper is instantiated once per process. Same
        // rationale as MsalBackedCredentialVault in ConfigServiceCollectionExtensions.
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<ConfigPaths>();
            var env = sp.GetRequiredService<IEnvironmentReader>();
            var logger = sp.GetRequiredService<ILogger<DataverseTokenCacheBinder>>();
            return DataverseTokenCacheBinder
                .CreateAsync(paths, env, logger)
                .GetAwaiter().GetResult();
        });

        services.AddSingleton<IConnectionProvider, DataverseConnectionProvider>();
        services.AddSingleton<IDataverseAccessTokenService, DataverseAccessTokenService>();
        services.AddSingleton<IDataverseConnectionFactory, DataverseConnectionFactory>();
        services.AddSingleton<IDataverseLiveChecker, DataverseLiveChecker>();
        services.AddSingleton<IInteractiveLoginService, DataverseInteractiveLoginService>();
        services.AddSingleton<ISolutionInventoryService, DataverseSolutionInventoryService>();
        services.AddSingleton<IDataPackageService, DataverseDataPackageService>();
        services.AddSingleton<ISolutionUninstallService, DataverseSolutionUninstallService>();
        services.AddSingleton<ISolutionImportService, DataverseSolutionImportService>();
        services.AddSingleton<IDeploymentHistoryService, DataverseDeploymentHistoryService>();
        services.AddSingleton<IDeploymentDetailService, DataverseDeploymentDetailService>();
        services.AddSingleton<IPackageImportService, DataversePackageImportService>();
        services.AddSingleton<TALXIS.CLI.Config.Bootstrapping.IConnectionProviderBootstrapper,
            TALXIS.CLI.Config.Bootstrapping.DataverseConnectionProviderBootstrapper>();
        return services;
    }
}
