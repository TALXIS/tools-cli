using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Http;
using TALXIS.CLI.Platform.PowerPlatform.Control.PowerAutomate;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.DependencyInjection;

public static class PowerPlatformConnectivityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Power Automate connector metadata services. Needs
    /// <c>IConfigurationResolver</c>, <c>IPowerPlatformEnvironmentCatalog</c>
    /// and <c>IAccessTokenService</c> from the composition root.
    /// </summary>
    public static IServiceCollection AddTxcPowerPlatformConnectivity(this IServiceCollection services)
    {
        services.AddSingleton<IEnvironmentIdResolver, EnvironmentIdResolver>();
        services.AddSingleton<RetryingHttpSender>();

        services.AddSingleton<PowerAutomateApiClient>();
        services.AddSingleton<IConnectorMetadataService, ConnectorMetadataService>();
        return services;
    }
}
