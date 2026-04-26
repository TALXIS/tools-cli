using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Services;

namespace TALXIS.CLI.Platform.Dataverse.Application.DependencyInjection;

public static class DataverseApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the application-plane Dataverse services (solution import /
    /// uninstall, package import / uninstall, deployment history, data
    /// packages, inventory).  Call after <c>AddTxcDataverseProvider()</c>.
    /// </summary>
    public static IServiceCollection AddTxcDataverseApplication(this IServiceCollection services)
    {
        services.AddSingleton<ISolutionInventoryService, DataverseSolutionInventoryService>();
        services.AddSingleton<IDataPackageService, DataverseDataPackageService>();
        services.AddSingleton<ISolutionUninstallService, DataverseSolutionUninstallService>();
        services.AddSingleton<ISolutionImportService, DataverseSolutionImportService>();
        services.AddSingleton<IDeploymentHistoryService, DataverseDeploymentHistoryService>();
        services.AddSingleton<IDeploymentDetailService, DataverseDeploymentDetailService>();
        services.AddSingleton<IPackageImportService, DataversePackageImportService>();
        services.AddSingleton<IPackageUninstallService, DataversePackageUninstallService>();
        services.AddTransient<IDataverseEntityMetadataService, DataverseEntityMetadataService>();
        services.AddTransient<IDataverseRelationshipService, DataverseRelationshipService>();
        services.AddTransient<IDataverseOptionSetService, DataverseOptionSetService>();
        services.AddSingleton<ISolutionDetailService, DataverseSolutionDetailService>();
        services.AddSingleton<ISolutionComponentQueryService, DataverseSolutionComponentQueryService>();
        services.AddSingleton<ISolutionDependencyService, DataverseSolutionDependencyService>();
        services.AddSingleton<ISolutionLayerQueryService, DataverseSolutionLayerQueryService>();
        services.AddSingleton<ISolutionCreateService, DataverseSolutionCreateService>();
        services.AddSingleton<ISolutionPublishService, DataverseSolutionPublishService>();
        services.AddSingleton<ISolutionComponentMutationService, DataverseSolutionComponentMutationService>();
        services.AddSingleton<IPublisherService, DataversePublisherService>();
        return services;
    }
}
