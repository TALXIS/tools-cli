using System.Diagnostics;
#if TELEMETRY_ENABLED
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
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
#if TELEMETRY_ENABLED
    private static OpenTelemetry.Trace.TracerProvider? _tracerProvider;
#else
    private static IDisposable? _tracerProvider;
#endif
    private static bool _initialized;

    /// <summary>
    /// Initializes telemetry if all gates pass (build config, user opt-in, env var).
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    /// <param name="configEnabled">Value of <c>telemetry.enabled</c> from config file.</param>
    /// <param name="configConnectionString">Optional connection string override from config file.</param>
    /// <param name="entryPoint">Identifies the host: "cli" or "mcp".</param>
    /// <summary>
    /// Initializes telemetry. Always on in Release builds — the only gate is
    /// whether a connection string is available (embedded at build time or
    /// set via environment variable).
    /// </summary>
    /// <param name="configConnectionString">Optional connection string override from config file.</param>
    /// <param name="entryPoint">Identifies the host: "cli" or "mcp".</param>
    public static void Initialize(string? configConnectionString = null, string entryPoint = "cli")
    {
        if (_initialized) return;
        _initialized = true;

#if !TELEMETRY_ENABLED
        // Debug/local builds — no telemetry, no exporter loaded
        return;
#else
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
    /// <para>
    /// For short-lived CLI processes this is critical: the OTel batch exporter fires
    /// every 5 seconds by default, so a command that completes in &lt;5s would silently
    /// lose all spans without an explicit flush before disposal.
    /// </para>
    /// </summary>
    public static void Shutdown()
    {
#if TELEMETRY_ENABLED
        // Explicit ForceFlush with bounded timeout before Dispose — ensures the
        // batch exporter sends pending spans even for sub-second CLI commands.
        // Dispose() also calls ForceFlush internally, but with an unbounded timeout
        // that could hang; the explicit call gives us a predictable upper bound.
        try
        {
            _tracerProvider?.ForceFlush(timeoutMilliseconds: 15000);
        }
        catch (Exception)
        {
            // Best-effort — never block process exit on telemetry failures
        }
#endif
        _tracerProvider?.Dispose();
        _tracerProvider = null;
    }

#if TELEMETRY_ENABLED
    private static OpenTelemetry.Trace.TracerProvider CreateTracerProvider(string connectionString, string entryPoint)
    {
        // Use OpenTelemetry SDK to create a TracerProvider with Azure Monitor exporter.
        // This is loaded via reflection-free direct API calls — the NuGet packages
        // are referenced by the entry-point projects (CLI, MCP).
        var builder = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(TxcTelemetry.Source.Name)
            .SetResourceBuilder(
                OpenTelemetry.Resources.ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: $"talxis-{entryPoint}",
                        serviceVersion: TxcTelemetry.Source.Version,
                        serviceInstanceId: Environment.MachineName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["talxis.cli.entry_point"] = entryPoint,
                        ["talxis.cli.is_ci"] = TxcTelemetry.IsRunningInCi(),
                        ["os.type"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    }))
            .AddHttpClientInstrumentation(opts =>
            {
                // Filter out the Azure Monitor exporter's own telemetry upload calls
                // to avoid a feedback loop where we log our own logging HTTP requests.
                opts.FilterHttpRequestMessage = req =>
                    req.RequestUri is null ||
                    !req.RequestUri.Host.Contains(".applicationinsights.azure.com");
            })
            .AddAzureMonitorTraceExporter(opts =>
            {
                opts.ConnectionString = connectionString;
                // Disable ALL SDK-side sampling — export 100% of spans.
                // Server-side ingestion sampling is configured in Azure Portal
                // (currently "All data 100%"), so this ensures nothing is dropped.
                // TracesPerSecond must be set to null because it overrides SamplingRatio
                // when both are specified (Azure Monitor exporter default enables
                // rate-limited sampling via TracesPerSecond).
                opts.SamplingRatio = 1.0f;
                opts.TracesPerSecond = null;
            });

        return (OpenTelemetry.Trace.TracerProvider)builder.Build()!;
    }
#endif
}
