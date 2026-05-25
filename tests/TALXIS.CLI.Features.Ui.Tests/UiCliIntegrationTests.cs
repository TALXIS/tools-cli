using Xunit;

namespace TALXIS.CLI.Features.Ui.Tests;

public class UiCliIntegrationTests
{
    [Fact]
    public async Task UiHelp_ShowsSessionAndBrowserSubcommands()
    {
        var output = await CliRunner.RunAsync(["ui", "--help"]);

        Assert.Contains("session", output);
        Assert.Contains("browser", output);
    }
}
