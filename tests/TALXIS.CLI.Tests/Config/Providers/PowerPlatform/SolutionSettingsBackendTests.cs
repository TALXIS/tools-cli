using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class SolutionSettingsBackendTests
{
    [Fact]
    public void ParseSettingList_NormalResponse_ReturnsSettings()
    {
        var json = """
        {
            "SettingDetailCollection": [
                { "Name": "EnableFormInsights", "Value": "true", "DataType": 2 },
                { "Name": "appcopilotenabled", "Value": "0", "DataType": 0 },
                { "Name": "SyncFrequency", "Value": "false", "DataType": 2 }
            ]
        }
        """;

        var result = SolutionSettingsBackend.ParseSettingList(json);

        Assert.Equal(3, result.Count);
        var byName = result.ToDictionary(s => s.Name, s => s.Value);
        Assert.Equal("true", byName["EnableFormInsights"]);
        Assert.Equal("0", byName["appcopilotenabled"]);
        Assert.Equal("false", byName["SyncFrequency"]);
    }

    [Fact]
    public void ParseSettingList_MissingCollection_ReturnsEmpty()
    {
        var result = SolutionSettingsBackend.ParseSettingList("""{ "other": 123 }""");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSettingList_EmptyCollection_ReturnsEmpty()
    {
        var result = SolutionSettingsBackend.ParseSettingList("""{ "SettingDetailCollection": [] }""");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSettingList_SkipsEntriesWithMissingName()
    {
        var json = """
        {
            "SettingDetailCollection": [
                { "Name": "ValidSetting", "Value": "yes" },
                { "Value": "orphan" },
                { "Name": "", "Value": "empty" },
                { "Name": "   ", "Value": "whitespace" }
            ]
        }
        """;

        var result = SolutionSettingsBackend.ParseSettingList(json);
        Assert.Single(result);
        Assert.Equal("ValidSetting", result[0].Name);
    }

    [Fact]
    public void ParseSettingList_HandlesNullValue()
    {
        var json = """
        {
            "SettingDetailCollection": [
                { "Name": "NullSetting", "Value": null }
            ]
        }
        """;

        var result = SolutionSettingsBackend.ParseSettingList(json);
        Assert.Single(result);
        Assert.Null(result[0].Value);
    }
}
