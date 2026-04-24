using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Platform.Dataverse.Runtime.DependencyInjection;

namespace TALXIS.CLI.Platform.Dataverse.Application.DependencyInjection;

/// <summary>
/// Composition root for a Config core + Dataverse provider container.
/// Idempotent. Called by:
/// <list type="bullet">
///   <item>The <c>TALXIS.CLI</c> host on startup.</item>
///   <item>The Package Deployer / CMT subprocess entry points that run
///         as their own mini-hosts.</item>
/// </list>
/// Lives alongside <see cref="DataverseProviderServiceCollectionExtensions"/>
/// because the Dataverse platform adapter owns its composition; feature
/// projects never touch this.
/// </summary>
public static class TxcServicesBootstrap
{
    public static void EnsureInitialized()
    {
        if (TxcServices.IsInitialized) return;

        var services = new ServiceCollection();
        services.AddTxcLogging();
        services.AddTxcConfigCore();
        services.AddTxcDataverseProvider();
        services.AddTxcDataverseApplication();

        var provider = services.BuildServiceProvider();
        TxcServices.Initialize(provider);
    }
}
