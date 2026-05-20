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
        _client = ResolveClient();
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
    /// Detects which MCP client / host environment is driving this process.
    /// </summary>
    private static string ResolveClient()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COPILOT_RUN_APP")))
            return "copilot-cli";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDECODE")))
            return "claude-code";
        return "terminal";
    }
}
#endif
