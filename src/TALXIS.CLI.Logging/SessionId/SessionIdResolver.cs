namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// Resolves a terminal/client session identifier by trying an ordered list of
/// strategies. The first non-null result wins. The resolved ID and its source
/// are cached for the process lifetime.
///
/// Consumed by <see cref="TxcTelemetrySetup"/> to set <c>txc.session_id</c> and
/// <c>txc.session_id.source</c> as resource attributes on the TracerProvider.
/// </summary>
public sealed class SessionIdResolver
{
    /// <summary>
    /// Default strategy chain, ordered by priority:
    /// <list type="number">
    ///   <item>Explicit <c>TXC_SESSION_ID</c> env var (CI, MCP→CLI propagation)</item>
    ///   <item>GitHub Copilot CLI <c>COPILOT_AGENT_SESSION_ID</c></item>
    ///   <item>Claude Code <c>CLAUDE_CODE_SESSION_ID</c></item>
    ///   <item>Terminal session vars (<c>TERM_SESSION_ID</c>, <c>WT_SESSION</c>)</item>
    ///   <item>Fallback UUID (always succeeds)</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<ISessionIdStrategy> DefaultStrategies =
    [
        new ExplicitEnvVarStrategy(),
        new CopilotSessionStrategy(),
        new ClaudeCodeSessionStrategy(),
        new TerminalSessionStrategy(),
        new FallbackStrategy(),
    ];

    private readonly IReadOnlyList<ISessionIdStrategy> _strategies;
    private string? _cachedSessionId;
    private string? _cachedSource;
    private bool _resolved;

    public SessionIdResolver() : this(DefaultStrategies) { }

    /// <summary>
    /// Creates a resolver with a custom strategy chain. Useful for testing.
    /// </summary>
    public SessionIdResolver(IReadOnlyList<ISessionIdStrategy> strategies)
    {
        _strategies = strategies;
    }

    /// <summary>
    /// The resolved session ID. Cached after first call.
    /// </summary>
    public string SessionId
    {
        get
        {
            EnsureResolved();
            return _cachedSessionId!;
        }
    }

    /// <summary>
    /// Which strategy provided the session ID (e.g. "copilot", "explicit", "generated").
    /// </summary>
    public string Source
    {
        get
        {
            EnsureResolved();
            return _cachedSource!;
        }
    }

    private void EnsureResolved()
    {
        if (_resolved) return;

        foreach (var strategy in _strategies)
        {
            var result = strategy.TryResolve();
            if (result != null)
            {
                _cachedSessionId = result;
                _cachedSource = strategy.Source;
                _resolved = true;
                return;
            }
        }

        // Should never happen if FallbackStrategy is included, but just in case
        _cachedSessionId = Guid.NewGuid().ToString("D");
        _cachedSource = "generated";
        _resolved = true;
    }
}
