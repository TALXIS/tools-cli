using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Platform.Dataverse.Data.DependencyInjection;

/// <summary>
/// Registers the data-plane Dataverse services (query, record CRUD, bulk
/// operations, file operations). Call after <c>AddTxcDataverseProvider()</c>.
/// </summary>
public static class DataverseDataServiceCollectionExtensions
{
    public static IServiceCollection AddTxcDataverseData(this IServiceCollection services)
    {
        services.AddSingleton<IDataverseQueryService, DataverseQueryService>();
        services.AddSingleton<IDataverseRecordService, DataverseRecordService>();
        services.AddSingleton<IDataverseBulkService, DataverseBulkService>();
        services.AddSingleton<IDataverseFileService, DataverseFileService>();
        services.AddTransient<IChangesetApplier, ChangesetApplier>();
        return services;
    }
}
