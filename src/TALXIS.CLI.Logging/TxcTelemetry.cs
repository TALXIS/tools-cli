using System.Diagnostics;
using System.Reflection;
using TALXIS.CLI.Abstractions;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Central telemetry entry point for the TALXIS CLI.
/// Provides connection string resolution and CI detection.
/// The canonical <see cref="ActivitySource"/> lives in <see cref="TxcActivitySource"/>
/// (Abstractions) so both Core and Logging share a single instance.
///
/// <para><b>Opt-in model:</b> Telemetry is always on for published (Release) builds.
/// The only gate is whether a connection string is available (embedded at build time
/// or set via environment variable). Debug/local builds skip initialization entirely
/// via <c>#if TELEMETRY_ENABLED</c>.</para>
///
/// <para>Connection string resolution (highest priority wins):</para>
/// <list type="number">
///   <item>Environment variable <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c></item>
///   <item>Config file <c>telemetry.connectionString</c></item>
///   <item>Built-in default injected at Release build time via <c>TxcTelemetryConnectionString</c> MSBuild property</item>
/// </list>
/// </summary>
public static class TxcTelemetry
{
    /// <summary>
    /// The single <see cref="ActivitySource"/> for all CLI/MCP instrumentation.
    /// Delegates to <see cref="TxcActivitySource.Instance"/> — kept here for
    /// backward compatibility and discoverability within the Logging layer.
    /// </summary>
    public static ActivitySource Source => TxcActivitySource.Instance;

    /// <summary>
    /// Environment variable that tags telemetry with pipeline/CI context.
    /// Not an opt-out — telemetry still flows, but spans are tagged for filtering.
    /// </summary>
    public const string CiEnvVar = "TXC_CI";

    /// <summary>
    /// Standard Azure SDK environment variable for App Insights connection string.
    /// Takes highest priority when resolving the connection string.
    /// </summary>
    public const string ConnectionStringEnvVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Resolves the effective App Insights connection string.
    /// Returns null if no connection string is available (telemetry cannot be initialized).
    /// </summary>
    /// <param name="configConnectionString">Optional user-provided override from config file.</param>
    public static string? ResolveConnectionString(string? configConnectionString = null)
    {
        // 1. Environment variable (highest priority — enterprise/CI override)
        var envValue = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        // 2. Config file value
        if (!string.IsNullOrWhiteSpace(configConnectionString))
            return configConnectionString;

        // 3. Built-in default (embedded at Release build time)
        return GetBuiltInConnectionString();
    }

    /// <summary>
    /// Returns true if the CLI is running in a CI/pipeline environment.
    /// Used to tag telemetry spans, not to disable them.
    /// </summary>
    public static bool IsRunningInCi()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CiEnvVar))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }

    /// <summary>
    /// Returns the built-in connection string embedded at build time via MSBuild
    /// assembly metadata, or null when not set (Debug/local builds).
    /// Pipeline injects: <c>-p:TxcTelemetryConnectionString="InstrumentationKey=..."</c>
    /// </summary>
    private static string? GetBuiltInConnectionString()
    {
        return typeof(TxcTelemetry).Assembly
            .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
            .OfType<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "TxcTelemetryConnectionString")
            ?.Value;
    }

}
