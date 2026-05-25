using System.Diagnostics;

namespace TALXIS.CLI.Abstractions;

/// <summary>
/// Canonical <see cref="ActivitySource"/> for all CLI/MCP instrumentation.
/// Both Core (<see cref="TALXIS.CLI.Core.Telemetry.CommandActivityScope"/>) and
/// Logging (<c>TxcTelemetry</c>) reference this single instance so the OTel SDK
/// captures spans from one source — no duplicate registration, no version drift.
/// </summary>
public static class TxcActivitySource
{
    /// <summary>
    /// Well-known source name registered with the OTel <c>TracerProvider</c>.
    /// Used by <c>.AddSource(Name)</c> in the telemetry setup.
    /// </summary>
    public const string Name = "TALXIS.CLI";

    /// <summary>
    /// The single <see cref="ActivitySource"/> for all CLI/MCP instrumentation.
    /// Always safe to use regardless of telemetry state — <c>StartActivity()</c>
    /// returns null when no listener is registered (zero overhead).
    /// </summary>
    public static readonly ActivitySource Instance = new(Name, GetVersion());

    /// <summary>
    /// Returns the current operation ID (W3C trace ID as 32-char hex) from the
    /// ambient <see cref="Activity"/>. Returns null when no Activity is active
    /// (e.g. telemetry disabled in Debug builds).
    /// Centralizes the <c>Activity.Current?.TraceId.ToHexString()</c> pattern
    /// used across multiple projects for consistent operation ID resolution.
    /// </summary>
    public static string? CurrentOperationId =>
        Activity.Current?.TraceId.ToHexString();

    private static string GetVersion()
    {
        // All projects in the solution share the same version from Directory.Build.props.
        // Using the Abstractions assembly version as the canonical telemetry version
        // avoids drift between Core and Logging assemblies.
        return typeof(TxcActivitySource).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
