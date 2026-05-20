namespace TALXIS.CLI.Logging.SessionId;

/// <summary>
/// Lowest-priority fallback: generates a new UUID. Always succeeds.
/// For long-lived MCP servers, this means all tool calls during one server
/// lifetime share the same session ID. For short-lived CLI invocations,
/// each run gets a unique ID (no cross-invocation correlation).
/// </summary>
public sealed class FallbackStrategy : ISessionIdStrategy
{
    public string Source => "generated";

    public string? TryResolve() => Guid.NewGuid().ToString("D");
}
