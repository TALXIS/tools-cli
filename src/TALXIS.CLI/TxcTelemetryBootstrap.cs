using TALXIS.CLI.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Platform.Dataverse.Application.DependencyInjection;

namespace TALXIS.CLI;

/// <summary>
/// Shared startup helper for loading the global config and initializing telemetry
/// for CLI-hosted entry points without duplicating bootstrap logic.
/// </summary>
public static class TxcTelemetryBootstrap
{
    private static readonly string TelemetryNotice =
        "TALXIS CLI collects usage data to help improve the tool.\n" +
        "Read more: " + TxcConstants.TelemetryDocUrl;

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

            // Apply stored log settings (log.level, log.format) as env-var defaults
            // so TxcLoggerFactory picks them up. Env vars take priority over config.
            config.Log?.ApplyAsEnvironmentDefaults();

            TxcTelemetrySetup.Initialize(
                configConnectionString: config.Telemetry.ConnectionString,
                entryPoint: entryPoint,
                configOptOut: config.Telemetry.OptOut);

            // Show the first-run telemetry notice once for interactive CLI sessions.
            // MCP entry point uses stdout for JSON-RPC — never write human text there.
            ShowTelemetryNoticeIfNeeded(config, configStore, entryPoint);
        }
        catch (Exception)
        {
            // Telemetry initialization must never prevent the host from starting.
            // No logger is guaranteed to be available at this stage.
            return;
        }
    }

    /// <summary>
    /// Prints a one-time telemetry notice to stderr on the first interactive CLI run.
    /// Skipped for MCP entry point (JSON-RPC on stdout) and when already shown.
    /// </summary>
    private static void ShowTelemetryNoticeIfNeeded(
        GlobalConfig config, IGlobalConfigStore configStore, string entryPoint)
    {
        if (config.Telemetry.NoticeShown) return;
        if (!string.Equals(entryPoint, "cli", StringComparison.OrdinalIgnoreCase)) return;

        // Telemetry notice is written directly to stderr — loggers may not be
        // initialized yet, and this must not appear in stdout (JSON/MCP output).
#pragma warning disable RS0030 // Approved: one-time startup notice to stderr before loggers are ready
        Console.Error.WriteLine();
        Console.Error.WriteLine(TelemetryNotice);
        Console.Error.WriteLine();
#pragma warning restore RS0030

        // Persist the flag so the notice is not shown again.
        config.Telemetry.NoticeShown = true;
        try
        {
#pragma warning disable RS0030 // Synchronous save — acceptable during one-time startup notice
            configStore.SaveAsync(config, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore RS0030
        }
        catch (Exception)
        {
            // Best-effort — if save fails the notice will show again next time.
        }
    }
}
