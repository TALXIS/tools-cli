using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connector.Operation;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class ParameterViewsTests
{
    private static OperationDetail Teams() => new(
        "shared_teams", "/providers/Microsoft.PowerApps/apis/shared_teams", "PostMessageToConversation", "Post message", null,
        "OpenApiConnection", false,
        [
            new("poster", "string", true, "Post as", ["Flow bot", "User"], null, null, null, null, null),
            new("location", "string", true, "Post in", null, null, null,
                DynamicValues: new DynamicValuesRef("GetMessageLocations",
                    new Dictionary<string, string?> { ["messageType"] = "ParentMessage", ["poster"] = "$ref:poster" }),
                DynamicTree: null, DynamicSchema: null),
            new("body", "object", true, "Message", null, null, null, null, null,
                DynamicSchema: new DynamicValuesRef("GetUnifiedActionSchema",
                    new Dictionary<string, string?> { ["poster"] = "$ref:poster", ["recipientType"] = "$ref:location" })),
        ],
        ResponseSchema: null);

    [Fact]
    public void DynamicParameter_SaysWhatItDependsOnAndHowToGetItsValues()
    {
        var body = Assert.Single(ParameterViews.From(Teams()), p => p.Name == "body");

        Assert.Equal(ParameterSource.Fields, body.ValuesFrom);
        Assert.Equal(["poster", "location"], body.DependsOn);
        Assert.Equal(
            "environment connector operation parameter get shared_teams PostMessageToConversation body --value poster=<poster> --value location=<location>",
            body.GetValues);
    }

    [Fact]
    public void LiteralLookupArguments_AreNotDependencies()
    {
        var location = Assert.Single(ParameterViews.From(Teams()), p => p.Name == "location");

        Assert.Equal(ParameterSource.List, location.ValuesFrom);
        Assert.Equal(["poster"], location.DependsOn);
    }

    [Fact]
    public void StaticParameter_HasNoLookup()
    {
        var poster = Assert.Single(ParameterViews.From(Teams()), p => p.Name == "poster");

        Assert.Null(poster.ValuesFrom);
        Assert.Null(poster.GetValues);
        Assert.Equal(["Flow bot", "User"], poster.AllowedValues!.Select(v => v.Value));
    }
}
