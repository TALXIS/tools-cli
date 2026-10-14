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
    public const string ErrorMessage = "txc.error_message";

    // MCP-specific
    public const string Tool = "txc.tool";
    public const string SubprocessExitCode = "txc.subprocess.exit_code";

    // Environment context
    public const string EnvironmentUrl = "txc.environment_url";
    public const string EnvironmentName = "txc.environment_name";

    // User identity — OTel enduser.* attributes map to App Insights built-in
    // fields (user_AuthenticatedId via enduser.id) and also appear in
    // customDimensions for Kusto queries.
    public const string EndUserId = "enduser.id";
    public const string EndUserIdDimension = "txc.end_user_id";
    public const string EndUserName = "enduser.name";
    public const string EndUserScope = "enduser.scope";

    // Session tracking — correlates all CLI/MCP activity from one terminal session.
    // Stamped on every span by SessionIdActivityProcessor.
    public const string SessionId = "txc.session_id";
    public const string SessionIdSource = "txc.session_id.source";

    // Client identification — which MCP client or host is driving this process
    public const string Client = "txc.client";

    // Entry point values
    public const string EntryPointCli = "cli";
    public const string EntryPointMcp = "mcp";
}
