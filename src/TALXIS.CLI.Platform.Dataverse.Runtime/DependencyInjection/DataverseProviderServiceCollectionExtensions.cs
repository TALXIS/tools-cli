using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Platform.Dataverse.Runtime.Authority;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Platform.Dataverse.Runtime.Msal;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Governance;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerPlatformRbac;
using TALXIS.CLI.Platform.PowerPlatform.Control.Strategies;
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
        services.AddSingleton<MicrosoftGraphClient>();
        services.AddSingleton<PowerPlatformRbacClient>();
        services.AddSingleton<PowerPlatformEnvironmentGroupClient>();
        services.AddSingleton<IPowerPlatformEnvironmentGroupClient>(sp => sp.GetRequiredService<PowerPlatformEnvironmentGroupClient>());
        services.AddSingleton<PowerPlatformEnvironmentGroupRoleClient>();
        services.AddSingleton<IPowerPlatformEnvironmentGroupRoleClient>(sp => sp.GetRequiredService<PowerPlatformEnvironmentGroupRoleClient>());
        services.AddSingleton<PowerPlatformPolicyRuleClient>();
        services.AddSingleton<IPowerPlatformPolicyRuleClient>(sp => sp.GetRequiredService<PowerPlatformPolicyRuleClient>());
        services.AddSingleton<PowerPlatformRbacRoleStrategy>();
        services.AddSingleton<BapAdminApplicationRoleStrategy>();
        services.AddSingleton<IPowerPlatformRoleAssignmentStrategy>(sp => sp.GetRequiredService<PowerPlatformRbacRoleStrategy>());
        services.AddSingleton<IPowerPlatformRoleAssignmentStrategy>(sp => sp.GetRequiredService<BapAdminApplicationRoleStrategy>());
        services.AddSingleton<IPowerPlatformEnvironmentCatalog, PowerPlatformEnvironmentCatalog>();
        services.AddSingleton<IPowerPlatformEnvironmentProvisioner, PowerPlatformEnvironmentProvisioner>();
        services.AddSingleton<EnvironmentSettingsClient>();
        services.AddSingleton<SecurityRoleResolver>();
        services.AddSingleton<TALXIS.CLI.Core.Platforms.PowerPlatform.IEnvironmentSettingsService,
            EnvironmentSettingsService>();
        services.AddSingleton<TALXIS.CLI.Core.Platforms.PowerPlatform.IEnvironmentManagementService,
            EnvironmentManagementService>();
        services.AddSingleton<TALXIS.CLI.Core.Platforms.PowerPlatform.IEnvironmentUserProvisioningService,
            EnvironmentUserProvisioningService>();
        services.AddSingleton<IDataverseLiveChecker, DataverseLiveChecker>();
        services.AddSingleton<IEnvironmentTypeResolver, DataverseEnvironmentTypeResolver>();
        services.AddSingleton<IInteractiveLoginService, DataverseInteractiveLoginService>();
        services.AddSingleton<IDeviceCodeLoginService, DataverseDeviceCodeLoginService>();
        services.AddSingleton<TALXIS.CLI.Core.Bootstrapping.IConnectionProviderBootstrapper,
            TALXIS.CLI.Platform.Dataverse.Runtime.Bootstrapping.DataverseConnectionProviderBootstrapper>();
        return services;
    }
}
