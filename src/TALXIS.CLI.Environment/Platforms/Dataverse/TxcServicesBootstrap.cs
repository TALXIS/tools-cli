using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Providers.Dataverse.DependencyInjection;

namespace TALXIS.CLI.Environment.Platforms.Dataverse;

/// <summary>
/// Shared <see cref="TxcServices"/> bootstrap used by both the main CLI pipeline
/// and by out-of-process helpers such as
/// <see cref="LegacyAssemblyHostSubprocess"/>. Idempotent.
/// </summary>
public static class TxcServicesBootstrap
{
    public static void EnsureInitialized()
    {
        if (TxcServices.IsInitialized) return;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTxcConfigCore();
        services.AddTxcDataverseProvider();

        var provider = services.BuildServiceProvider();
        TxcServices.Initialize(provider);
    }
}
