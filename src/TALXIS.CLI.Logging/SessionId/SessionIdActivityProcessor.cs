using System.Diagnostics;
#if TELEMETRY_ENABLED
using OpenTelemetry;
#endif

namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// OpenTelemetry span processor that stamps session and client context on every span.
/// Ensures <c>txc.session_id</c>, <c>txc.session_id.source</c>, and <c>txc.client</c>
/// appear in App Insights <c>customDimensions</c> (not just as resource attributes).
/// </summary>
#if TELEMETRY_ENABLED
public sealed class SessionIdActivityProcessor : BaseProcessor<Activity>
{
    private readonly string _sessionId;
    private readonly string _sessionSource;
    private readonly string _client;

    public SessionIdActivityProcessor(SessionIdResolver resolver)
    {
        _sessionId = resolver.SessionId;
        _sessionSource = resolver.Source;
        // Derive client from the session ID source rather than checking a separate
        // set of env vars. This keeps client detection aligned with session strategies
        // — adding a new client only requires a new ISessionIdStrategy, not a separate
        // if-chain here.
        _client = DeriveClient(_sessionSource);
    }

    public override void OnStart(Activity data)
    {
        data.SetTag("txc.session_id", _sessionId);
        data.SetTag("txc.session_id.source", _sessionSource);
        data.SetTag("txc.client", _client);

        // Azure Monitor exporter maps this tag to the native session_Id column,
        // enabling built-in Sessions and User Flows in the App Insights portal.
        data.SetTag("microsoft.session.id", _sessionId);
    }

    /// <summary>
    /// Maps the session ID source (from <see cref="ISessionIdStrategy.Source"/>) to
    /// a client identifier for the <c>txc.client</c> telemetry tag.
    /// </summary>
    internal static string DeriveClient(string sessionSource) => sessionSource switch
    {
        "copilot" => "copilot-cli",
        "claude-code" => "claude-code",
        // "explicit" means the parent MCP server propagated the ID — the real client
        // is determined by the original source, but at the subprocess level we can
        // fall back to env-var detection for backward compatibility.
        "explicit" => DetectExplicitClient(),
        _ => "terminal",
    };

    /// <summary>
    /// Fallback detection for "explicit" source (MCP→CLI propagation) where the
    /// original client is not encoded in the session source.
    /// </summary>
    private static string DetectExplicitClient()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COPILOT_RUN_APP")))
            return "copilot-cli";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDECODE")))
            return "claude-code";
        return "terminal";
    }
}
#endif
