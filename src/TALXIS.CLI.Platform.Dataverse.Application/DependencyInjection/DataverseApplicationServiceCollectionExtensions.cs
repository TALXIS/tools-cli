using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Platforms.Dataverse;
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
        return services;
    }
}
