namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// Resolves the session ID from well-known terminal session environment variables.
/// Covers direct CLI usage from terminals that set a per-window/tab identifier:
/// <list type="bullet">
///   <item><c>TERM_SESSION_ID</c> — macOS Terminal.app</item>
///   <item><c>WT_SESSION</c> — Windows Terminal</item>
/// </list>
/// </summary>
public sealed class TerminalSessionStrategy : ISessionIdStrategy
{
    private static readonly string[] TerminalSessionVars =
    [
        "TERM_SESSION_ID",
        "WT_SESSION",
    ];

    public string Source => "terminal";

    public string? TryResolve()
    {
        foreach (var envVar in TerminalSessionVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
