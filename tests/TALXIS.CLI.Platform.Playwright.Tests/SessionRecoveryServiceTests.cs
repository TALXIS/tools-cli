using Xunit;

namespace TALXIS.CLI.Platform.Playwright.Tests;

public class SessionRecoveryServiceTests
{
    [Fact]
    public void TryBuildRecoveryUrl_ExtractsSafeMainUrl()
    {
        var currentUrl = "https://org.crm4.dynamics.com/errorhandler.aspx?BackUri="
            + Uri.EscapeDataString("https://org.crm4.dynamics.com/main.aspx?appid=warehouse-app");

        var safeUrl = SessionRecoveryService.TryBuildRecoveryUrl(currentUrl);

        Assert.Equal("https://org.crm4.dynamics.com/main.aspx?appid=warehouse-app", safeUrl);
    }
}
