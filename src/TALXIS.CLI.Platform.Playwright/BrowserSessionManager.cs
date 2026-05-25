using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Browser;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Platform.Playwright;

public sealed class BrowserSessionManager : IBrowserSessionManager
{
    private static readonly JsonSerializerOptions SessionSerializerOptions = TxcJsonOptions.Default;
    private static readonly Dictionary<string, ReAuthDialogWatcher> Watchers = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConfigPaths _paths;
    private readonly StorageStateManager _storageStateManager;
    private readonly SessionRecoveryService _sessionRecoveryService;
    private readonly ILogger<BrowserSessionManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactoryWrapper _httpFactory;

    public BrowserSessionManager(
        ConfigPaths paths,
        StorageStateManager storageStateManager,
        SessionRecoveryService sessionRecoveryService,
        ILogger<BrowserSessionManager> logger,
        ILoggerFactory loggerFactory,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _paths = paths;
        _storageStateManager = storageStateManager;
        _sessionRecoveryService = sessionRecoveryService;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _httpFactory = httpFactory ?? DefaultHttpClientFactoryWrapper.Instance;
    }

    public async Task<BrowserSession> LaunchAsync(BrowserLaunchOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProfileName);

        var existing = await TryGetProfileSessionAsync(options.ProfileName, ct).ConfigureAwait(false);
        if (existing is not null)
            await CloseAsync(existing.Id, ct).ConfigureAwait(false);

        BrowserProfilePaths.EnsureProfileDirectories(_paths, options.ProfileName);
        var hasSavedState = await _storageStateManager.ExistsAsync(options.ProfileName, ct).ConfigureAwait(false);
        var effectiveHeadless = options.Headless && hasSavedState;
        if (options.Headless && !effectiveHeadless)
        {
            _logger.LogInformation(
                "Saved browser state was not found for profile '{ProfileName}'. Falling back to headed launch for interactive sign-in.",
                options.ProfileName);
        }

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        var browserType = ResolveBrowserType(playwright, options.BrowserType);
        var executablePath = browserType.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Playwright Chromium executable path could not be resolved. Install browsers with 'npx playwright install chromium'.");

        var port = ReservePort();
        var userDataDir = BrowserProfilePaths.UserDataDirectory(_paths, options.ProfileName);
        using var process = StartBrowserProcess(executablePath, userDataDir, port, options with { Headless = effectiveHeadless });

        var cdpEndpoint = await WaitForCdpEndpointAsync(port, ct).ConfigureAwait(false);
        await using var browser = await browserType.ConnectOverCDPAsync(cdpEndpoint).ConfigureAwait(false);
        var context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("Connected browser did not expose a default context.");
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(options.AppUrl))
        {
            await page.GotoAsync(
                options.AppUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 }).ConfigureAwait(false);
            await _sessionRecoveryService.CheckAndRecoverAsync(page, ct).ConfigureAwait(false);
            await _storageStateManager.SaveAsync(context, options.ProfileName, ct).ConfigureAwait(false);
            var watcher = new ReAuthDialogWatcher(_loggerFactory.CreateLogger<ReAuthDialogWatcher>());
            await watcher.StartAsync(page, CancellationToken.None).ConfigureAwait(false);
            lock (Watchers)
            {
                Watchers[options.ProfileName] = watcher;
            }
        }

        var session = new BrowserSession(
            Id: Guid.NewGuid().ToString("N")[..8],
            ProfileName: options.ProfileName,
            CdpEndpoint: cdpEndpoint,
            AppUrl: page.Url,
            CreatedAt: DateTime.UtcNow,
            Pid: process.Id,
            Headless: effectiveHeadless,
            BrowserType: options.BrowserType,
            UserDataDir: userDataDir);

        await WriteSessionAsync(session, ct).ConfigureAwait(false);
        return session;
    }

    public async Task<BrowserSession?> AttachAsync(string cdpEndpoint, CancellationToken ct)
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        var browser = await playwright.Chromium.ConnectOverCDPAsync(cdpEndpoint).ConfigureAwait(false);
        await browser.DisposeAsync().ConfigureAwait(false);

        var sessions = await ListSessionsAsync(ct).ConfigureAwait(false);
        return sessions.FirstOrDefault(session => string.Equals(session.CdpEndpoint, cdpEndpoint, StringComparison.OrdinalIgnoreCase));
    }

    public async Task CloseAsync(string sessionId, CancellationToken ct)
    {
        var session = await GetSessionAsync(sessionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

        try
        {
            using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
            await using var browser = await playwright.Chromium.ConnectOverCDPAsync(session.CdpEndpoint).ConfigureAwait(false);
            await browser.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Graceful browser close via CDP failed for session {SessionId}. Falling back to PID shutdown.", sessionId);
        }

        TryKillProcess(session.Pid);
        await DeleteSessionFileAsync(session.ProfileName, ct).ConfigureAwait(false);

        ReAuthDialogWatcher? watcher = null;
        lock (Watchers)
        {
            if (Watchers.TryGetValue(session.ProfileName, out watcher))
                Watchers.Remove(session.ProfileName);
        }

        if (watcher is not null)
            await watcher.DisposeAsync().ConfigureAwait(false);
    }

    public async Task<BrowserSession?> GetActiveSessionAsync(CancellationToken ct)
    {
        var sessions = await ListSessionsAsync(ct).ConfigureAwait(false);
        return sessions.OrderByDescending(session => session.CreatedAt).FirstOrDefault();
    }

    public async Task<BrowserSession?> GetSessionAsync(string sessionId, CancellationToken ct)
    {
        var sessions = await ListSessionsAsync(ct).ConfigureAwait(false);
        return sessions.FirstOrDefault(session => string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<BrowserSession>> ListSessionsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var root = BrowserProfilePaths.BrowserRoot(_paths);
        if (!Directory.Exists(root))
            return Array.Empty<BrowserSession>();

        var sessions = new List<BrowserSession>();
        foreach (var sessionFile in Directory.EnumerateFiles(root, "session.json", SearchOption.AllDirectories))
        {
            var session = await ReadSessionFileAsync(sessionFile, ct).ConfigureAwait(false);
            if (session is null)
                continue;

            if (!IsProcessRunning(session.Pid))
            {
                File.Delete(sessionFile);
                continue;
            }

            sessions.Add(session);
        }

        return sessions.OrderByDescending(session => session.CreatedAt).ToList();
    }

    public async Task<string?> GetCurrentUrlAsync(string sessionId, CancellationToken ct)
    {
        var session = await GetSessionAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null)
            return null;

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(session.CdpEndpoint).ConfigureAwait(false);
        var page = await GetPrimaryPageAsync(browser).ConfigureAwait(false);
        return page.Url;
    }

    public async Task<JsonElement> EvaluateAsync(string sessionId, string script, CancellationToken ct)
    {
        var session = await GetSessionAsync(sessionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        await using var browser = await playwright.Chromium.ConnectOverCDPAsync(session.CdpEndpoint).ConfigureAwait(false);
        var page = await GetPrimaryPageAsync(browser).ConfigureAwait(false);
        return await page.EvaluateAsync<JsonElement>(script).ConfigureAwait(false);
    }

    private static IBrowserType ResolveBrowserType(IPlaywright playwright, string browserType)
        => browserType.Equals("chromium", StringComparison.OrdinalIgnoreCase)
            ? playwright.Chromium
            : throw new NotSupportedException($"Browser type '{browserType}' is not supported in CP1. Use 'chromium'.");

    private async Task<BrowserSession?> TryGetProfileSessionAsync(string profileName, CancellationToken ct)
    {
        var path = BrowserProfilePaths.SessionFile(_paths, profileName);
        if (!File.Exists(path))
            return null;

        var session = await ReadSessionFileAsync(path, ct).ConfigureAwait(false);
        if (session is null || !IsProcessRunning(session.Pid))
        {
            if (File.Exists(path))
                File.Delete(path);
            return null;
        }

        return session;
    }

    private Process StartBrowserProcess(string executablePath, string userDataDir, int port, BrowserLaunchOptions options)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = options.Headless,
        };

        startInfo.ArgumentList.Add($"--remote-debugging-port={port}");
        startInfo.ArgumentList.Add($"--user-data-dir={userDataDir}");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--disable-popup-blocking");
        if (options.Headless)
        {
            startInfo.ArgumentList.Add("--headless=new");
            startInfo.ArgumentList.Add("--disable-gpu");
        }
        else
        {
            startInfo.ArgumentList.Add("--start-maximized");
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Chromium process.");
        return process;
    }

    private async Task<string> WaitForCdpEndpointAsync(int port, CancellationToken ct)
    {
        using var client = _httpFactory.Create();

        for (var attempt = 0; attempt < 60; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var payload = await client.GetStringAsync($"http://127.0.0.1:{port}/json/version", ct).ConfigureAwait(false);
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.TryGetProperty("webSocketDebuggerUrl", out var endpoint))
                {
                    var value = endpoint.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for Chromium's CDP endpoint.");
    }

    private async Task WriteSessionAsync(BrowserSession session, CancellationToken ct)
    {
        var path = BrowserProfilePaths.SessionFile(_paths, session.ProfileName);
        await JsonFile.WriteAtomicAsync(path, session, ct).ConfigureAwait(false);
    }

    private async Task DeleteSessionFileAsync(string profileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = BrowserProfilePaths.SessionFile(_paths, profileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static async Task<BrowserSession?> ReadSessionFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BrowserSession>(stream, SessionSerializerOptions, ct).ConfigureAwait(false);
    }

    private static async Task<IPage> GetPrimaryPageAsync(IBrowser browser)
    {
        var context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("Connected browser did not expose a default context.");

        return context.Pages.FirstOrDefault() ?? await context.NewPageAsync().ConfigureAwait(false);
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void TryKillProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
        }
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
