using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connector.Operation;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class FlowSnippetTests
{
    private static OperationParameter Param(
        string name, string type = "string", bool required = true,
        IReadOnlyList<string>? allowed = null, IReadOnlyDictionary<string, string>? labels = null,
        DynamicValuesRef? schema = null)
        => new(name, type, required, Description: name, AllowedValues: allowed, DefaultValue: null, Visibility: null,
            DynamicValues: null, DynamicTree: null, DynamicSchema: schema, ValueLabels: labels);

    private static OperationDetail Trigger() => new(
        "shared_commondataserviceforapps", "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps",
        "SubscribeWebhookTrigger", "When a row is added, modified or deleted", null, "OpenApiConnectionWebhook", false,
        [
            Param("subscriptionRequest/message", "integer", allowed: ["1", "3"], labels: new Dictionary<string, string> { ["3"] = "Modified" }),
            Param("subscriptionRequest/entityname", allowed: ["account", "contact"]),
            Param("subscriptionRequest/filteringattributes", required: false),
        ],
        ResponseSchema: null, IsTrigger: true);

    [Fact]
    public void Trigger_GoesUnderTriggers_InTheExportedShape()
    {
        var snippet = FlowSnippet.Build(Trigger(), "mtj_dataverse",
            new Dictionary<string, string> { ["subscriptionRequest/message"] = "3", ["subscriptionRequest/entityname"] = "account" });

        var step = snippet["triggers"]!["When_a_row_is_added_modified_or_deleted"]!;
        Assert.Equal("OpenApiConnectionWebhook", (string?)step["type"]);
        Assert.Null(step["runAfter"]);
        Assert.Equal("@parameters('$authentication')", (string?)step["inputs"]!["authentication"]);
        Assert.Equal("shared_commondataserviceforapps", (string?)step["inputs"]!["host"]!["connectionName"]);

        // Integers go in as JSON numbers, as exports have them; optional parameters stay out.
        var parameters = step["inputs"]!["parameters"]!.AsObject();
        Assert.Equal(3, (long)parameters["subscriptionRequest/message"]!);
        Assert.Equal("account", (string?)parameters["subscriptionRequest/entityname"]);
        Assert.False(parameters.ContainsKey("subscriptionRequest/filteringattributes"));

        Assert.Equal("mtj_dataverse",
            (string?)snippet["connectionReferences"]!["shared_commondataserviceforapps"]!["connection"]!["connectionReferenceLogicalName"]);
    }

    [Fact]
    public void MissingRequiredValues_BecomePlaceholdersListingTheChoices()
    {
        var parameters = FlowSnippet.Build(Trigger())["triggers"]!["When_a_row_is_added_modified_or_deleted"]!["inputs"]!["parameters"]!;

        Assert.Equal("<subscriptionRequest/message: one of 1, 3 (Modified)>", (string?)parameters["subscriptionRequest/message"]);
    }

    [Fact]
    public void UnresolvedSubSchema_PointsToResolve()
    {
        var action = new OperationDetail("shared_commondataserviceforapps", "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps",
            "CreateRecord", "Add a new row", null, "OpenApiConnection", false,
            [Param("entityName"), Param("item", "object", schema: new DynamicValuesRef("GetMetadataForPostEntity", null))],
            ResponseSchema: null);

        var snippet = FlowSnippet.Build(action);
        var step = snippet["actions"]!["Add_a_new_row"]!;
        Assert.NotNull(step["runAfter"]);
        Assert.Contains("getValues", (string?)step["inputs"]!["parameters"]!["item/..."]);
    }
}
