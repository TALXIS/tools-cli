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

    private static string GetVersion()
    {
        // All projects in the solution share the same version from Directory.Build.props.
        // Using the Abstractions assembly version as the canonical telemetry version
        // avoids drift between Core and Logging assemblies.
        return typeof(TxcActivitySource).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
