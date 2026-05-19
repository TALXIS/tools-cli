namespace TALXIS.CLI.Core.Telemetry;

/// <summary>
/// Well-known Activity tag keys for txc telemetry.
/// Centralizes the telemetry schema so all hosts (CLI, MCP, future REST API)
/// use the same vocabulary. Prevents typos and makes tags discoverable.
/// </summary>
public static class TxcTelemetryTags
{
    // Command execution context
    public const string Command = "txc.command";
    public const string EntryPoint = "txc.entry_point";
    public const string Version = "txc.version";
    public const string ExitCode = "txc.exit_code";
    public const string ErrorKind = "txc.error_kind";

    // MCP-specific
    public const string Tool = "txc.tool";
    public const string SubprocessExitCode = "txc.subprocess.exit_code";

    // Environment context
    public const string EnvironmentUrl = "txc.environment_url";
    public const string EnvironmentName = "txc.environment_name";

    // User identity — using txc.* namespace because OTel enduser.* attributes
    // are mapped to App Insights built-in fields and may be stripped from customDimensions
    public const string EndUserId = "txc.user_id";
    public const string EndUserName = "txc.user_name";
    public const string EndUserScope = "txc.tenant_id";

    // Entry point values
    public const string EntryPointCli = "cli";
    public const string EntryPointMcp = "mcp";
}
