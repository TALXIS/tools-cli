using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Platform.Dataverse.Application.DependencyInjection;

namespace TALXIS.CLI;

/// <summary>
/// Shared startup helper for loading the global config and initializing telemetry
/// for CLI-hosted entry points without duplicating bootstrap logic.
/// </summary>
public static class TxcTelemetryBootstrap
{
    public static void Initialize(string entryPoint, bool ensureServices = false)
    {
        try
        {
            if (ensureServices)
            {
                TxcServicesBootstrap.EnsureInitialized();
            }

            var configStore = TxcServices.Get<IGlobalConfigStore>();
#pragma warning disable RS0030 // Synchronous telemetry init before async main loop / host.RunAsync
            var config = configStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore RS0030
            TxcTelemetrySetup.Initialize(
                configConnectionString: config.Telemetry.ConnectionString,
                entryPoint: entryPoint);
        }
        catch (Exception)
        {
            // Telemetry initialization must never prevent the host from starting.
            // No logger is guaranteed to be available at this stage.
            return;
        }
    }
}
