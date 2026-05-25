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
/// <para>Telemetry is on by default for published (Release) builds. Users can
/// opt out via <c>TXC_TELEMETRY_OPTOUT=1</c> environment variable or the
/// <c>telemetry.optOut</c> config setting. Debug/local builds skip initialization
/// entirely via <c>#if TELEMETRY_ENABLED</c>. See TELEMETRY.md for full details.</para>
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
    /// Environment variable to opt out of telemetry collection.
    /// Any truthy value (<c>1</c>, <c>true</c>, <c>yes</c>) disables telemetry.
    /// Takes priority over the config file setting.
    /// </summary>
    public const string OptOutEnvVar = "TXC_TELEMETRY_OPTOUT";

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
    /// Returns true if the user has opted out of telemetry via the
    /// <c>TXC_TELEMETRY_OPTOUT</c> environment variable or the config file flag.
    /// Environment variable takes priority over config.
    /// </summary>
    public static bool IsOptedOut(bool configOptOut = false)
    {
        var envValue = Environment.GetEnvironmentVariable(OptOutEnvVar);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue == "1"
                || string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(envValue, "yes", StringComparison.OrdinalIgnoreCase);
        }
        return configOptOut;
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
