using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace TALXIS.CLI.Platform.Playwright;

public sealed class SessionRecoveryService
{
    private readonly ILogger<SessionRecoveryService> _logger;

    public SessionRecoveryService(ILogger<SessionRecoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> CheckAndRecoverAsync(IPage page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (string.IsNullOrWhiteSpace(page.Url) || !page.Url.Contains("errorhandler.aspx", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var safeUrl = TryBuildRecoveryUrl(page.Url);
            if (safeUrl is null)
                return false;

            await page.EvaluateAsync("() => { try { sessionStorage.clear(); } catch {} }").ConfigureAwait(false);
            await page.GotoAsync(
                safeUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 }).ConfigureAwait(false);
            await page.WaitForURLAsync("**/main.aspx**", new PageWaitForURLOptions { Timeout = 30000 }).ConfigureAwait(false);
            await page.Locator("[role='menuitem']").First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 }).ConfigureAwait(false);

            _logger.LogInformation("Recovered from errorhandler.aspx — navigated to {SafeUrl}", safeUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recover from errorhandler.aspx.");
            return false;
        }
    }

    internal static string? TryBuildRecoveryUrl(string currentUrl)
    {
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri))
            return null;

        var query = ParseQuery(currentUri.Query);
        if (!query.TryGetValue("BackUri", out var backUriRaw) || string.IsNullOrWhiteSpace(backUriRaw))
            return null;

        var decodedBackUri = Uri.UnescapeDataString(backUriRaw);
        if (!Uri.TryCreate(decodedBackUri, UriKind.Absolute, out var backUri))
            return null;

        var backQuery = ParseQuery(backUri.Query);
        if (!backQuery.TryGetValue("appid", out var appId) || string.IsNullOrWhiteSpace(appId))
            return null;

        return $"{currentUri.GetLeftPart(UriPartial.Authority)}/main.aspx?appid={Uri.EscapeDataString(appId)}";
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0)
                continue;

            result[pair[..separator]] = pair[(separator + 1)..];
        }

        return result;
    }
}
