using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;
using TALXIS.CLI.Platform.PowerPlatform.Control.Http;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.DependencyInjection;

public static class PowerPlatformConnectivityServiceCollectionExtensions
{
    /// <summary>
    /// Registers connector, connection and Power Automate connector metadata services.
    /// Every collaborator is a singleton, so there is one connector catalog
    /// cache per process. Needs <c>IConfigurationResolver</c>,
    /// <c>IPowerPlatformEnvironmentCatalog</c>, <c>IDataverseQueryService</c>,
    /// <c>IAccessTokenService</c> and <c>MicrosoftGraphClient</c> from the
    /// composition root.
    /// </summary>
    public static IServiceCollection AddTxcPowerPlatformConnectivity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IEnvironmentIdResolver, EnvironmentIdResolver>();
        services.AddSingleton<RetryingHttpSender>();
        services.AddSingleton<ConnectivityTargetResolver>();
        services.AddSingleton<ConnectivityApiClient>();
        services.AddSingleton<ConnectorCatalogClient>();
        services.AddSingleton<ConnectorLogicalNameLookup>();
        services.AddSingleton<SilentConsentHelper>();
        services.AddSingleton<PrincipalResolver>();

        services.AddSingleton<ConnectorResolver>();
        services.AddSingleton<IConnectorResolver>(sp => sp.GetRequiredService<ConnectorResolver>());
        services.AddSingleton<IConnectorService, ConnectorCatalogService>();

        services.AddSingleton<ConnectionReader>();
        services.AddSingleton<IConnectionReader>(sp => sp.GetRequiredService<ConnectionReader>());
        services.AddSingleton<ConnectionConsentFlow>();
        services.AddSingleton<ConnectionProvisioner>();
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IConnectionPermissionService, ConnectionPermissionService>();

        services.AddSingleton<PowerAutomateApiClient>();
        services.AddSingleton<IConnectorMetadataService, ConnectorMetadataService>();
        return services;
    }
}
