namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// Resolves the session ID from <c>CLAUDE_CODE_SESSION_ID</c>, set by Claude Code
/// in Bash/PowerShell tool subprocesses and hook commands.
/// Whether this variable reaches stdio MCP servers depends on Claude Code's
/// environment propagation — it may only be set for direct subprocesses.
/// If not available, falls through to later strategies.
/// </summary>
public sealed class ClaudeCodeSessionStrategy : ISessionIdStrategy
{
    public const string EnvVar = "CLAUDE_CODE_SESSION_ID";

    public string Source => "claude-code";

    public string? TryResolve()
    {
        var value = Environment.GetEnvironmentVariable(EnvVar);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
