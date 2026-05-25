using System.Diagnostics;
using TALXIS.CLI.Abstractions;
using TALXIS.CLI.Logging.SessionId;
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
    private static SessionIdResolver? _sessionIdResolver;

    /// <summary>
    /// The session ID resolver used during initialization.
    /// Exposed so the MCP subprocess runner can propagate the resolved session ID
    /// to child CLI processes via the <c>TXC_SESSION_ID</c> environment variable.
    /// Null before <see cref="Initialize"/> is called.
    /// </summary>
    public static SessionIdResolver? SessionResolver => _sessionIdResolver;

    /// <summary>
    /// Initializes telemetry. On by default for Release builds — gated by
    /// connection string availability and opt-out flag.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    /// <param name="configConnectionString">Optional connection string from the user's config file
    /// (<c>telemetry.connectionString</c>). Falls back to build-time embedded value if null.</param>
    /// <param name="entryPoint">Identifies how the process was reached: <c>"cli"</c> or <c>"mcp"</c>.
    /// Emitted as telemetry metadata and used as the default service suffix unless the host overrides it.</param>
    /// <param name="configOptOut">Opt-out flag from the user's config file
    /// (<c>telemetry.optOut</c>). Environment variable <c>TXC_TELEMETRY_OPTOUT</c> takes priority.</param>
    public static void Initialize(string? configConnectionString = null, string entryPoint = "cli", bool configOptOut = false)
    {
        if (_initialized) return;
        _initialized = true;

        // Resolve session ID early — always available even without telemetry
        // so the MCP subprocess runner can propagate it to child processes.
        _sessionIdResolver = new SessionIdResolver();
        TxcSupportInfo.SetSessionId(_sessionIdResolver.SessionId);
        System.Diagnostics.Trace.TraceInformation(
            $"Session ID resolved: {_sessionIdResolver.SessionId} (source: {_sessionIdResolver.Source})");

#if !TELEMETRY_ENABLED
        // Debug/local builds — no telemetry, no exporter loaded
        return;
#else
        if (TxcTelemetry.IsOptedOut(configOptOut))
            return;

        var connectionString = TxcTelemetry.ResolveConnectionString(configConnectionString);
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        try
        {
            _tracerProvider = CreateTracerProvider(connectionString, entryPoint, _sessionIdResolver);
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
    private static OpenTelemetry.Trace.TracerProvider CreateTracerProvider(
        string connectionString, string entryPoint, SessionIdResolver sessionResolver)
    {
        var serviceSuffix = ResolveServiceSuffix(entryPoint);

        // Use OpenTelemetry SDK to create a TracerProvider with Azure Monitor exporter.
        // This is loaded via reflection-free direct API calls — the NuGet packages
        // are referenced by the entry-point projects (CLI, MCP).
        var builder = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(TxcTelemetry.Source.Name)
            .SetResourceBuilder(
                OpenTelemetry.Resources.ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: $"talxis-{serviceSuffix}",
                        serviceVersion: TxcTelemetry.Source.Version,
                        serviceInstanceId: Environment.MachineName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["talxis.cli.entry_point"] = entryPoint,
                        ["talxis.cli.is_ci"] = TxcTelemetry.IsRunningInCi(),
                        ["os.type"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        ["txc.session_id"] = sessionResolver.SessionId,
                        ["txc.session_id.source"] = sessionResolver.Source,
                    }))
            // HTTP client instrumentation intentionally omitted. Command-level activity
            // spans already capture CLI operations end-to-end. HTTP dependency tracking
            // added noise — Azure Monitor exporter's own POST /v2.1/track calls leaked
            // through both FilterHttpRequestMessage and TelemetryFilterProcessor (the
            // Azure.Core HttpPipeline bypasses standard HttpClient instrumentation hooks,
            // and clearing ActivityTraceFlags.Recorded in OnEnd is too late — the exporter
            // has already captured the span data). Removing this eliminates the self-tracking
            // feedback loop without losing useful telemetry.
            .AddProcessor(new SessionIdActivityProcessor(sessionResolver))
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

    /// <summary>
    /// Resolves the OTel service name suffix. Defaults to <paramref name="entryPoint"/>
    /// unless overridden by <c>TXC_SERVICE_SUFFIX</c> (set by the MCP subprocess runner
    /// so child CLI processes identify as <c>talxis-cli</c> instead of <c>talxis-mcp</c>).
    /// </summary>
    internal static string ResolveServiceSuffix(string entryPoint)
    {
        var overrideSuffix = Environment.GetEnvironmentVariable("TXC_SERVICE_SUFFIX");
        return string.IsNullOrWhiteSpace(overrideSuffix) ? entryPoint : overrideSuffix;
    }
}
