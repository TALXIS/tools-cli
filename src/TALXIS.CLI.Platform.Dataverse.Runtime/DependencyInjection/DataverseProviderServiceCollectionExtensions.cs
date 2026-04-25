using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Platform.Dataverse.Runtime.Authority;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Platform.Dataverse.Runtime.Msal;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Platform.Dataverse.Runtime.DependencyInjection;

public static class DataverseProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Dataverse runtime infrastructure: MSAL client factory,
    /// authority-challenge resolver, token-cache binder, connection provider,
    /// access-token service, connection factory, live checker, interactive
    /// login, bootstrapper, and the Power Platform environment catalog.
    /// Must be called after <c>AddTxcConfigCore()</c>.
    /// </summary>
    /// <remarks>
    /// Application-plane service implementations (solution import, deployment
    /// history, etc.) are registered separately via
    /// <c>AddTxcDataverseApplicationServices()</c> in
    /// <see cref="TALXIS.CLI.Platform.Dataverse.Application"/> .
    /// </remarks>
    public static IServiceCollection AddTxcDataverseProvider(this IServiceCollection services)
    {
        services.AddSingleton<MsalClientFactory>();
        services.AddSingleton<AuthorityChallengeResolver>();

        // Singleton so MsalCacheHelper is instantiated once per process. Same
        // rationale as MsalBackedCredentialVault in ConfigServiceCollectionExtensions.
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<ConfigPaths>();
            var env = sp.GetRequiredService<IEnvironmentReader>();
            var logger = sp.GetRequiredService<ILogger<MsalTokenCacheBinder>>();
#pragma warning disable RS0030 // Sync-over-async required: DI service factory lambda is synchronous
            return MsalTokenCacheBinder
                .CreateAsync(paths, env, logger)
                .GetAwaiter().GetResult();
#pragma warning restore RS0030
        });
        services.AddSingleton<ITokenCacheStore>(sp => sp.GetRequiredService<MsalTokenCacheBinder>());

        services.AddSingleton<IConnectionProvider, DataverseConnectionProvider>();
        services.AddSingleton<DataverseAccessTokenService>();
        services.AddSingleton<IDataverseAccessTokenService>(sp => sp.GetRequiredService<DataverseAccessTokenService>());
        services.AddSingleton<IAccessTokenService>(sp => sp.GetRequiredService<DataverseAccessTokenService>());
        services.AddSingleton<IDataverseConnectionFactory, DataverseConnectionFactory>();
        services.AddSingleton<IPowerPlatformEnvironmentCatalog, PowerPlatformEnvironmentCatalog>();
        services.AddSingleton<EnvironmentSettingsClient>();
        services.AddSingleton<TALXIS.CLI.Core.Platforms.PowerPlatform.IEnvironmentSettingsService,
            EnvironmentSettingsService>();
        services.AddSingleton<IDataverseLiveChecker, DataverseLiveChecker>();
        services.AddSingleton<IEnvironmentTypeResolver, DataverseEnvironmentTypeResolver>();
        services.AddSingleton<IInteractiveLoginService, DataverseInteractiveLoginService>();
        services.AddSingleton<TALXIS.CLI.Core.Bootstrapping.IConnectionProviderBootstrapper,
            TALXIS.CLI.Platform.Dataverse.Runtime.Bootstrapping.DataverseConnectionProviderBootstrapper>();
        return services;
    }
}
