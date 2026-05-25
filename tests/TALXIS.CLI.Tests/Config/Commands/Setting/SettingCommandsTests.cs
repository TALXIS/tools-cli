using System.IO;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Features.Config.Setting;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Commands.Setting;

[Collection("TxcServicesSerial")]
public sealed class SettingCommandsTests
{
    [Fact]
    public async Task Set_Persists_LogLevel()
    {
        using var host = new CommandTestHost();

        var exit = await new SettingSetCliCommand { Key = "log.level", Value = "Debug" }.RunAsync();
        Assert.Equal(0, exit);

        var store = TxcServices.Get<IGlobalConfigStore>();
        var cfg = await store.LoadAsync(default);
        Assert.Equal("debug", cfg.Log.Level);
    }

    [Fact]
    public async Task Set_Persists_LogFormat_AndIsCaseInsensitive()
    {
        using var host = new CommandTestHost();

        var exit = await new SettingSetCliCommand { Key = "LOG.FORMAT", Value = "JSON" }.RunAsync();
        Assert.Equal(0, exit);

        var cfg = await TxcServices.Get<IGlobalConfigStore>().LoadAsync(default);
        Assert.Equal("json", cfg.Log.Format);
    }

    [Fact]
    public async Task Set_ReturnsExit2_ForUnknownKey()
    {
        using var host = new CommandTestHost();

        var exit = await new SettingSetCliCommand { Key = "bogus.key", Value = "x" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Set_ReturnsExit2_ForInvalidEnumValue()
    {
        using var host = new CommandTestHost();

        var exit = await new SettingSetCliCommand { Key = "log.level", Value = "shout" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Set_ReturnsExit2_ForInvalidEnumValueOnLogFormat()
    {
        using var host = new CommandTestHost();

        var exit = await new SettingSetCliCommand { Key = "log.format", Value = "xml" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Get_PrintsDefaultValue_WhenUnset()
    {
        using var host = new CommandTestHost();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
        {
            exit = await new SettingGetCliCommand { Key = "log.level" }.RunAsync();
        }

        Assert.Equal(0, exit);
        var output = sw.ToString().Trim();
        // OutputFormatter.WriteValue emits JSON when output is redirected, plain text otherwise.
        Assert.Contains("information", output);
    }

    [Fact]
    public async Task Get_PrintsUpdatedValue_AfterSet()
    {
        using var host = new CommandTestHost();

        await new SettingSetCliCommand { Key = "log.format", Value = "json" }.RunAsync();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
        {
            exit = await new SettingGetCliCommand { Key = "log.format" }.RunAsync();
        }

        Assert.Equal(0, exit);
        var output = sw.ToString().Trim();
        // OutputFormatter.WriteValue emits JSON when output is redirected, plain text otherwise.
        Assert.Contains("json", output);
    }

    [Fact]
    public async Task Get_ReturnsExit2_ForUnknownKey()
    {
        using var host = new CommandTestHost();

        var exit = await new SettingGetCliCommand { Key = "bogus" }.RunAsync();
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task List_EmitsJson_ForAllKnownKeys()
    {
        using var host = new CommandTestHost();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
        {
            exit = await new SettingListCliCommand().RunAsync();
        }

        Assert.Equal(0, exit);
        var output = sw.ToString();
        Assert.Contains("log.level", output);
        Assert.Contains("log.format", output);
    }
}
