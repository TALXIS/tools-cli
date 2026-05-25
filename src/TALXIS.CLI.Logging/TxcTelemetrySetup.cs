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
    /// <param name="entryPoint">Identifies the host: <c>"cli"</c> or <c>"mcp"</c>.
    /// Used to set the OTel service name (<c>talxis-cli</c> vs <c>talxis-mcp</c>).</param>
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
                        ["txc.session_id"] = sessionResolver.SessionId,
                        ["txc.session_id.source"] = sessionResolver.Source,
                    }))
            .AddHttpClientInstrumentation(opts =>
            {
                // Filter out the Azure Monitor exporter's own telemetry upload calls
                // to avoid a feedback loop where we log our own logging HTTP requests.
                // This covers standard HttpClient usage; Azure.Core's HttpPipeline
                // may bypass this, so TelemetryFilterProcessor provides a second safety net.
                opts.FilterHttpRequestMessage = req =>
                {
                    var host = req.RequestUri?.Host ?? string.Empty;
                    return !IsAzureMonitorHost(host);
                };
            })
            .AddProcessor(new TelemetryFilterProcessor())
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

    /// <summary>
    /// Returns true for Azure Monitor / App Insights ingestion hosts.
    /// Used by both the HTTP filter and the fallback processor.
    /// </summary>
    internal static bool IsAzureMonitorHost(string host)
    {
        return host.Contains(".applicationinsights.azure.com")
            || host.Contains(".livediagnostics.monitor.azure.com")
            || host.Contains(".visualstudio.com");
    }

    /// <summary>
    /// Safety-net processor that drops HTTP dependency spans targeting Azure Monitor
    /// endpoints. Covers cases where Azure.Core's HttpPipeline bypasses the
    /// standard <c>FilterHttpRequestMessage</c> hook.
    /// </summary>
    private sealed class TelemetryFilterProcessor : BaseProcessor<System.Diagnostics.Activity>
    {
        public override void OnEnd(System.Diagnostics.Activity data)
        {
            // HTTP spans set "url.full" or "http.url" or have the host in the display name.
            // The most reliable tag set by the OTel HTTP instrumentation is "server.address"
            // or the URL-related tags.
            var serverAddress = data.GetTagItem("server.address") as string;
            if (serverAddress != null && IsAzureMonitorHost(serverAddress))
            {
                data.ActivityTraceFlags &= ~System.Diagnostics.ActivityTraceFlags.Recorded;
                return;
            }

            // Fallback: check the span's display name for the host pattern
            if (data.DisplayName != null
                && (data.DisplayName.Contains(".applicationinsights.azure.com")
                    || data.DisplayName.Contains(".livediagnostics.monitor.azure.com")))
            {
                data.ActivityTraceFlags &= ~System.Diagnostics.ActivityTraceFlags.Recorded;
            }
        }
    }
#endif
}
