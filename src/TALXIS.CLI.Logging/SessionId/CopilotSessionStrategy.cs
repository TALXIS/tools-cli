namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// Resolves the session ID from <c>COPILOT_AGENT_SESSION_ID</c>, set by the
/// GitHub Copilot CLI for each agent session. Verified to be present in the
/// MCP server process environment when spawned by Copilot CLI.
/// </summary>
public sealed class CopilotSessionStrategy : ISessionIdStrategy
{
    public const string EnvVar = "COPILOT_AGENT_SESSION_ID";

    public string Source => "copilot";

    public string? TryResolve()
    {
        var value = Environment.GetEnvironmentVariable(EnvVar);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
