using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TALXIS.CLI.Core.Storage;
using Xunit;

namespace TALXIS.CLI.Platform.Playwright.Tests;

public class BrowserSessionManagerTests
{
    [Fact]
    public void BrowserProfilePaths_UsesProfileScopedDirectories()
    {
        var paths = new ConfigPaths("/tmp/txc-tests");

        var profileDirectory = BrowserProfilePaths.ProfileDirectory(paths, "dev/profile");
        var userDataDirectory = BrowserProfilePaths.UserDataDirectory(paths, "dev/profile");
        var sessionFile = BrowserProfilePaths.SessionFile(paths, "dev/profile");

        Assert.Equal(Path.Combine("/tmp/txc-tests", "browser", "dev-profile"), profileDirectory);
        Assert.Equal(Path.Combine("/tmp/txc-tests", "browser", "dev-profile", "user-data"), userDataDirectory);
        Assert.Equal(Path.Combine("/tmp/txc-tests", "browser", "dev-profile", "session.json"), sessionFile);
    }

    [Fact]
    public async Task LaunchAsync_WritesSessionFile_WhenChromiumIsAvailable()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        if (string.IsNullOrWhiteSpace(playwright.Chromium.ExecutablePath) || !File.Exists(playwright.Chromium.ExecutablePath))
            return;

        using var temp = new TempConfigDir();
        var storageStateManager = new StorageStateManager(temp.Paths, new StorageStateManagerTests.FakeVault());
        var context = new Mock<IBrowserContext>();
        context.Setup(value => value.StorageStateAsync()).ReturnsAsync("{\"cookies\":[],\"origins\":[]}");
        await storageStateManager.SaveAsync(context.Object, "integration", CancellationToken.None);

        var manager = new BrowserSessionManager(
            temp.Paths,
            storageStateManager,
            new SessionRecoveryService(NullLogger<SessionRecoveryService>.Instance),
            NullLogger<BrowserSessionManager>.Instance,
            NullLoggerFactory.Instance);

        var session = await manager.LaunchAsync(
            new TALXIS.CLI.Core.Browser.BrowserLaunchOptions("integration", "about:blank", Headless: true),
            CancellationToken.None);

        Assert.True(File.Exists(BrowserProfilePaths.SessionFile(temp.Paths, "integration")));
        Assert.True(session.Headless);

        await manager.CloseAsync(session.Id, CancellationToken.None);
    }

    private sealed class TempConfigDir : IDisposable
    {
        public TempConfigDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "txc-browser-session-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Root);
            Paths = new ConfigPaths(Root);
        }

        public string Root { get; }
        public ConfigPaths Paths { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
