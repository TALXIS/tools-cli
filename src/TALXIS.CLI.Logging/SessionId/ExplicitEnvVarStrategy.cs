namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// Highest-priority strategy: checks <c>TXC_SESSION_ID</c> environment variable.
/// Used for explicit overrides (CI pipelines, custom scripts) and for MCP→CLI
/// propagation where the MCP server sets this on child processes.
/// </summary>
public sealed class ExplicitEnvVarStrategy : ISessionIdStrategy
{
    public const string EnvVar = "TXC_SESSION_ID";

    /// <summary>
    /// When MCP propagates the session ID to a child CLI process, it also sets
    /// this env var so the child reports the original source (e.g. "copilot")
    /// instead of "explicit".
    /// </summary>
    public const string SourceEnvVar = "TXC_SESSION_ID_SOURCE";

    public string Source
    {
        get
        {
            // If the parent process told us which strategy originally resolved the ID,
            // use that instead of "explicit" so the whole transaction is consistent.
            var propagatedSource = Environment.GetEnvironmentVariable(SourceEnvVar);
            return string.IsNullOrWhiteSpace(propagatedSource) ? "explicit" : propagatedSource;
        }
    }

    public string? TryResolve()
    {
        var value = Environment.GetEnvironmentVariable(EnvVar);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
