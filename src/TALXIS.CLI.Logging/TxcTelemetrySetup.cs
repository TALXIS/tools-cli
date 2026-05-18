using System.Diagnostics;
#if TELEMETRY_ENABLED
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
#endif

namespace TALXIS.CLI.Logging;

/// <summary>
/// Initializes and manages the OpenTelemetry TracerProvider for the CLI process.
/// Call <see cref="Initialize"/> once at startup; call <see cref="Shutdown"/> at process exit.
///
/// Telemetry is on by default for all published (Release) builds.
/// Debug builds skip initialization entirely — no NuGet dependency loaded, no network calls.
/// </summary>
public static class TxcTelemetrySetup
{
    private static IDisposable? _tracerProvider;
    private static bool _initialized;

    /// <summary>
    /// Initializes telemetry if all gates pass (build config, user opt-in, env var).
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    /// <param name="configEnabled">Value of <c>telemetry.enabled</c> from config file.</param>
    /// <param name="configConnectionString">Optional connection string override from config file.</param>
    /// <param name="entryPoint">Identifies the host: "cli" or "mcp".</param>
    public static void Initialize(bool configEnabled, string? configConnectionString = null, string entryPoint = "cli")
    {
        if (_initialized) return;
        _initialized = true;

#if !TELEMETRY_ENABLED
        // Debug/local builds — no telemetry, no exporter loaded
        return;
#else
        if (!TxcTelemetry.ShouldEnable(configEnabled))
            return;

        var connectionString = TxcTelemetry.ResolveConnectionString(configConnectionString);
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        try
        {
            _tracerProvider = CreateTracerProvider(connectionString, entryPoint);
        }
        catch (Exception)
        {
            // Telemetry initialization must never crash the CLI.
            // Silently degrade to no-telemetry mode.
            _tracerProvider = null;
            return;
        }
#endif
    }

    /// <summary>
    /// Flushes pending telemetry and shuts down the provider.
    /// Call at process exit for clean shutdown. Safe if not initialized.
    /// </summary>
    public static void Shutdown()
    {
        _tracerProvider?.Dispose();
        _tracerProvider = null;
    }

#if TELEMETRY_ENABLED
    private static IDisposable CreateTracerProvider(string connectionString, string entryPoint)
    {
        // Use OpenTelemetry SDK to create a TracerProvider with Azure Monitor exporter.
        // This is loaded via reflection-free direct API calls — the NuGet packages
        // are referenced by the entry-point projects (CLI, MCP).
        var builder = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(TxcTelemetry.Source.Name)
            .SetResourceBuilder(
                OpenTelemetry.Resources.ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: "talxis-cli",
                        serviceVersion: TxcTelemetry.Source.Version,
                        serviceInstanceId: Environment.MachineName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["talxis.cli.entry_point"] = entryPoint,
                        ["talxis.cli.is_ci"] = TxcTelemetry.IsRunningInCi(),
                        ["os.type"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    }))
            .AddHttpClientInstrumentation()
            .AddAzureMonitorTraceExporter(opts =>
            {
                opts.ConnectionString = connectionString;
            });

        return builder.Build()!;
    }
#endif
}
