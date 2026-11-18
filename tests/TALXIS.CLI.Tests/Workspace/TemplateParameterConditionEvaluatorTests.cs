using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.Workspace;

public class TemplateParameterConditionEvaluatorTests
{
    // The conditional parameters of pp-entity, as declared in its template.json
    private static readonly List<(string, string?, string?)> EntityParams = new()
    {
        ("LogicalName", null, null),
        ("IsSystemEntity", null, "false"),
        ("SystemPublisherPrefix", "(IsSystemEntity == true)", null),
        ("PublisherPrefix", "(IsSystemEntity == false)", null),
    };

    private static IReadOnlyList<string> Select(params (string Key, string Value)[] provided)
        => TemplateParameterConditionEvaluator.SelectEnabled(
            EntityParams,
            provided.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void SystemEntity_ShowsSystemPrefix_HidesPublisherPrefix()
    {
        var enabled = Select(("IsSystemEntity", "true"));

        Assert.Contains("SystemPublisherPrefix", enabled);
        Assert.DoesNotContain("PublisherPrefix", enabled);
        Assert.Contains("LogicalName", enabled);
    }

    [Fact]
    public void CustomEntity_ShowsPublisherPrefix_HidesSystemPrefix()
    {
        var enabled = Select(("IsSystemEntity", "false"));

        Assert.Contains("PublisherPrefix", enabled);
        Assert.DoesNotContain("SystemPublisherPrefix", enabled);
    }

    [Fact]
    public void UnrelatedValue_FallsBackToDefault()
    {
        var enabled = Select(("LogicalName", "account"));

        Assert.Contains("PublisherPrefix", enabled);
        Assert.DoesNotContain("SystemPublisherPrefix", enabled);
    }

    [Fact]
    public void KeyMatchingIsCaseInsensitive_BoolValueToo()
    {
        var enabled = Select(("issystementity", "True"));

        Assert.Contains("SystemPublisherPrefix", enabled);
        Assert.DoesNotContain("PublisherPrefix", enabled);
    }

    // A slice of pp-entity-attribute's conditional parameters, as declared in its template.json
    private static readonly List<(string, string?, string?)> AttributeParams = new()
    {
        ("AttributeType", null, "Text"),
        ("MaxLength", "(AttributeType == \"Text\" || AttributeType == \"Autonumber\")", "100"),
        ("Precision", "(AttributeType == \"Decimal\" || AttributeType == \"Float\" || AttributeType == \"Money\")", "2"),
        ("GlobalOptionSetName", "(AttributeType == \"OptionSet(Global)\") || (AttributeType == \"OptionSet(GlobalMulti)\")", null),
        ("LookupTarget", "(AttributeType == \"Lookup\") || (AttributeType == \"Customer\")", null),
    };

    [Theory]
    [InlineData("Text", "MaxLength")]
    [InlineData("Float", "Precision")]
    [InlineData("OptionSet(GlobalMulti)", "GlobalOptionSetName")]
    [InlineData("Customer", "LookupTarget")]
    public void AttributeType_KeepsOnlyItsOwnParameters(string attributeType, string expected)
    {
        var enabled = TemplateParameterConditionEvaluator.SelectEnabled(
            AttributeParams,
            new Dictionary<string, string> { ["AttributeType"] = attributeType });

        Assert.Equal(new[] { "AttributeType", expected }, enabled);
    }

    [Fact]
    public void UnparsableCondition_KeepsParameter()
    {
        var enabled = TemplateParameterConditionEvaluator.SelectEnabled(
            new List<(string, string?, string?)> { ("Broken", "(IsSystemEntity ==", null) },
            new Dictionary<string, string> { ["IsSystemEntity"] = "true" });

        Assert.Contains("Broken", enabled);
    }
}
