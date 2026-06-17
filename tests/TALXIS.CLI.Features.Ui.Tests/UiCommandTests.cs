using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Ui.Browser;
using TALXIS.CLI.Features.Ui.Session;
using Xunit;

namespace TALXIS.CLI.Features.Ui.Tests;

public class UiCommandTests
{
    [Fact]
    public async Task SessionOpen_WithTypeAndParams_BuildsUrlAndLaunchesBrowserSession()
    {
        using var host = new UiCommandTestHost();
        using var writer = new StringWriter();
        using var redirect = OutputWriter.RedirectTo(writer);

        var command = new UiSessionOpenCliCommand
        {
            Type = "AppModule",
            Param = ["name=WarehouseApp"],
            Profile = "dev-profile",
            Format = "json",
        };

        var exitCode = await command.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.NotNull(host.BrowserManager.LastLaunchOptions);
        Assert.Equal("dev-profile", host.BrowserManager.LastLaunchOptions!.ProfileName);
        Assert.Contains("appname=WarehouseApp", host.BrowserManager.LastLaunchOptions.AppUrl);
        Assert.Contains("\"profileName\": \"dev-profile\"", writer.ToString());
    }

    [Fact]
    public async Task SessionStatus_WritesCurrentSessionData()
    {
        using var host = new UiCommandTestHost();
        using var writer = new StringWriter();
        using var redirect = OutputWriter.RedirectTo(writer);

        var command = new UiSessionStatusCliCommand { Format = "json" };
        var exitCode = await command.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains("\"id\": \"session-1\"", writer.ToString());
        Assert.Contains("\"appUrl\": \"https://contoso.crm4.dynamics.com/main.aspx?appid=test\"", writer.ToString());
    }

    [Fact]
    public async Task BrowserEval_WritesJsonAndTextFriendlyOutput()
    {
        using var host = new UiCommandTestHost();
        host.BrowserManager.EvalResult = JsonDocument.Parse("\"Document Title\"").RootElement.Clone();
        using var writer = new StringWriter();
        using var redirect = OutputWriter.RedirectTo(writer);

        var command = new UiBrowserEvalCliCommand { Eval = "document.title", Format = "json" };
        var exitCode = await command.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal("\"Document Title\"" + System.Environment.NewLine, writer.ToString());
    }
}
