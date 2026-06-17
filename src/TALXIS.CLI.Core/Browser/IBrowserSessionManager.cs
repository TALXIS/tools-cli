namespace TALXIS.CLI.Core.Browser;

public interface IBrowserSessionManager
{
    Task<BrowserSession> LaunchAsync(BrowserLaunchOptions options, CancellationToken ct);
    Task<BrowserSession?> AttachAsync(string cdpEndpoint, CancellationToken ct);
    Task CloseAsync(string sessionId, CancellationToken ct);
    Task<BrowserSession?> GetActiveSessionAsync(CancellationToken ct);
    Task<BrowserSession?> GetSessionAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<BrowserSession>> ListSessionsAsync(CancellationToken ct);
    Task<string?> GetCurrentUrlAsync(string sessionId, CancellationToken ct);
    Task<System.Text.Json.JsonElement> EvaluateAsync(string sessionId, string script, CancellationToken ct);
}
