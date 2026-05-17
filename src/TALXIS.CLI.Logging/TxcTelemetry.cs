using System.Diagnostics;
using System.Reflection;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Central telemetry entry point for the TALXIS CLI.
/// Owns the shared <see cref="ActivitySource"/> used by CLI commands and MCP tool dispatch.
/// 
/// Telemetry is opt-in:
/// <list type="number">
///   <item><b>Build-time gate:</b> <c>#if TELEMETRY_ENABLED</c> (Release builds only).</item>
///   <item><b>User opt-in:</b> <c>telemetry.enabled = true</c> in <c>~/.txc/config.json</c>.</item>
///   <item><b>Env var opt-out:</b> <c>TXC_TELEMETRY_OPTOUT=1</c> (overrides config).</item>
/// </list>
/// 
/// Connection string resolution (highest priority wins):
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
    /// Always safe to use regardless of telemetry state — <c>StartActivity()</c>
    /// returns null when no listener is registered (zero overhead).
    /// </summary>
    public static readonly ActivitySource Source = new("TALXIS.CLI", GetCliVersion());

    /// <summary>
    /// Environment variable that, when set to "1" or "true", disables telemetry
    /// regardless of config file settings. Intended for CI/pipeline use.
    /// </summary>
    public const string OptOutEnvVar = "TXC_TELEMETRY_OPTOUT";

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
    /// Checks whether telemetry should be active based on config and environment.
    /// </summary>
    /// <param name="configEnabled">The <c>telemetry.enabled</c> value from config file.</param>
    public static bool ShouldEnable(bool configEnabled)
    {
        // Env var opt-out always wins
        var optOut = Environment.GetEnvironmentVariable(OptOutEnvVar);
        if (string.Equals(optOut, "1", StringComparison.Ordinal) ||
            string.Equals(optOut, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        return configEnabled;
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

    private static string GetCliVersion()
    {
        var asm = typeof(TxcTelemetry).Assembly;
        var infoAttr = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();
        return infoAttr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "0.0.0";
    }
}
