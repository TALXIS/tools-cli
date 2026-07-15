using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace TALXIS.CLI.Platform.Playwright;

public sealed class ReAuthDialogWatcher : IAsyncDisposable
{
    private readonly ILogger<ReAuthDialogWatcher> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _recoveryDelay;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;

    public ReAuthDialogWatcher(
        ILogger<ReAuthDialogWatcher> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? recoveryDelay = null)
    {
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
        _recoveryDelay = recoveryDelay ?? TimeSpan.FromSeconds(1);
    }

    public Task StartAsync(IPage page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (_backgroundTask is not null)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _backgroundTask = RunAsync(page, _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        if (_backgroundTask is not null)
        {
            try
            {
                await _backgroundTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _backgroundTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(IPage page, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);

            try
            {
                var dialog = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Sign in to continue" });
                var visible = await dialog.IsVisibleAsync().ConfigureAwait(false);
                if (!visible)
                    continue;

                await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" })
                    .ClickAsync(new LocatorClickOptions { Timeout = 1000 }).ConfigureAwait(false);
                await Task.Delay(_recoveryDelay, ct).ConfigureAwait(false);
                _logger.LogInformation("Dismissed 'Sign in to continue' dialog");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Re-auth dialog watcher polling iteration failed.");
            }
        }
    }
}
