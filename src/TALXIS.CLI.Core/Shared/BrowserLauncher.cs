using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;

namespace TALXIS.CLI.Core;

/// <summary>
/// Cross-platform utility to open a URL in the default browser.
/// Headless-aware: skips browser launch in CI/non-interactive environments.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>
    /// Opens <paramref name="url"/> in the default browser.
    /// In headless/CI mode, logs a warning and returns without opening.
    /// </summary>
    public static void Open(Uri url, ILogger logger)
    {
        var detector = TxcServices.Get<IHeadlessDetector>();
        if (detector.IsHeadless)
        {
            logger.LogWarning("Headless mode ({Reason}) — browser not opened.", detector.Reason);
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url.AbsoluteUri);
            }
            else
            {
                Process.Start("xdg-open", url.AbsoluteUri);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not open browser: {Error}", ex.Message);
        }
    }
}
