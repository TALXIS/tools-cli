namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// Highest-priority strategy: checks <c>TXC_SESSION_ID</c> environment variable.
/// Used for explicit overrides (CI pipelines, custom scripts) and for MCP→CLI
/// propagation where the MCP server sets this on child processes.
/// </summary>
public sealed class ExplicitEnvVarStrategy : ISessionIdStrategy
{
    public const string EnvVar = "TXC_SESSION_ID";

    public string Source => "explicit";

    public string? TryResolve()
    {
        var value = Environment.GetEnvironmentVariable(EnvVar);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
