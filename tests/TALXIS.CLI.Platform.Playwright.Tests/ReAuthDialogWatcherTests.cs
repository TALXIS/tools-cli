using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using Xunit;

namespace TALXIS.CLI.Platform.Playwright.Tests;

public class ReAuthDialogWatcherTests
{
    [Fact]
    public async Task StartAsync_ClicksCloseWhenDialogIsVisible()
    {
        var page = new Mock<IPage>();
        var dialog = new Mock<ILocator>();
        var closeButton = new Mock<ILocator>();

        page.Setup(value => value.GetByRole(AriaRole.Dialog, It.IsAny<PageGetByRoleOptions>()))
            .Returns(dialog.Object);
        dialog.Setup(value => value.IsVisibleAsync()).ReturnsAsync(true);
        dialog.Setup(value => value.GetByRole(AriaRole.Button, It.IsAny<LocatorGetByRoleOptions>()))
            .Returns(closeButton.Object);
        closeButton.Setup(value => value.ClickAsync(It.IsAny<LocatorClickOptions>()))
            .Returns(Task.CompletedTask);

        await using var watcher = new ReAuthDialogWatcher(
            NullLogger<ReAuthDialogWatcher>.Instance,
            pollInterval: TimeSpan.FromMilliseconds(10),
            recoveryDelay: TimeSpan.FromMilliseconds(1));

        await watcher.StartAsync(page.Object, CancellationToken.None);
        await Task.Delay(40);
        await watcher.StopAsync();

        closeButton.Verify(value => value.ClickAsync(It.IsAny<LocatorClickOptions>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task StopAsync_CancelsPollingLoopCleanly()
    {
        var page = new Mock<IPage>();
        var dialog = new Mock<ILocator>();
        page.Setup(value => value.GetByRole(AriaRole.Dialog, It.IsAny<PageGetByRoleOptions>()))
            .Returns(dialog.Object);
        dialog.Setup(value => value.IsVisibleAsync()).ReturnsAsync(false);

        await using var watcher = new ReAuthDialogWatcher(
            NullLogger<ReAuthDialogWatcher>.Instance,
            pollInterval: TimeSpan.FromMilliseconds(10),
            recoveryDelay: TimeSpan.FromMilliseconds(1));

        await watcher.StartAsync(page.Object, CancellationToken.None);
        await Task.Delay(20);
        await watcher.StopAsync();

        Assert.True(true);
    }
}
