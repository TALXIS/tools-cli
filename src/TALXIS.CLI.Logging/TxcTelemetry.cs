using System.Diagnostics;
using System.Reflection;

namespace TALXIS.CLI.Logging;

/// <summary>
/// Central telemetry entry point for the TALXIS CLI.
/// Owns the shared <see cref="ActivitySource"/> used by CLI commands and MCP tool dispatch.
/// 
/// Telemetry is on by default for published builds:
/// <list type="number">
///   <item><b>Build-time gate:</b> <c>#if TELEMETRY_ENABLED</c> (Release builds only — Debug/local never emits).</item>
///   <item><b>On by default:</b> <c>telemetry.enabled = true</c> in <c>~/.txc/config.json</c>.</item>
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
    /// Checks whether telemetry should be active based on config.
    /// Telemetry is on by default; only disabled if the user explicitly sets
    /// <c>telemetry.enabled = false</c> in their config file.
    /// </summary>
    /// <param name="configEnabled">The <c>telemetry.enabled</c> value from config file.</param>
    public static bool ShouldEnable(bool configEnabled)
    {
        return configEnabled;
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

    private static string GetCliVersion()
    {
        // Use AssemblyVersion (e.g. "1.11.0") not InformationalVersion which
        // appends the git commit hash and is noisy in App Insights dashboards.
        return typeof(TxcTelemetry).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
