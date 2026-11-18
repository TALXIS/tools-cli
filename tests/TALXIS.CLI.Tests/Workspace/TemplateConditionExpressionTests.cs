using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.Workspace;

public class TemplateConditionExpressionTests
{
    private static readonly Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IsSystemEntity"] = "True",
        ["Kind"] = "Lookup",
        ["Count"] = "2",
    };

    [Theory]
    [InlineData("(IsSystemEntity == true)", true)]
    [InlineData("(IsSystemEntity == false)", false)]
    [InlineData("IsSystemEntity", true)]
    [InlineData("!IsSystemEntity", false)]
    [InlineData("!(IsSystemEntity == false)", true)]
    [InlineData("Kind == \"Lookup\"", true)]
    [InlineData("Kind == 'Customer'", false)]
    [InlineData("Kind != \"Customer\"", true)]
    [InlineData("(Kind == \"Customer\") || (Kind == \"Lookup\")", true)]
    [InlineData("Kind == \"Lookup\" && IsSystemEntity == false", false)]
    [InlineData("Count == 2", true)]
    [InlineData("Missing == \"\"", true)]
    public void Evaluates(string expression, bool expected)
    {
        Assert.Equal(expected, TemplateConditionExpression.TryEvaluate(expression, Vars));
    }

    // pp-entity-attribute's OptionSetName isRequired condition
    private const string OptionSetNameRequired =
        "(AttributeType == \"OptionSet(Local)\" || AttributeType == \"OptionSet(LocalMulti)\" || ((AttributeType == \"OptionSet(Global)\" || AttributeType == \"OptionSet(GlobalMulti)\") && OptionSetType == \"New\"))";

    [Theory]
    [InlineData("OptionSet(Local)", "Existing", true)]
    [InlineData("OptionSet(Global)", "New", true)]
    [InlineData("OptionSet(GlobalMulti)", "Existing", false)]
    [InlineData("Text", "New", false)]
    public void NestedTemplateCondition(string attributeType, string optionSetType, bool expected)
    {
        var vars = new Dictionary<string, string> { ["AttributeType"] = attributeType, ["OptionSetType"] = optionSetType };

        Assert.Equal(expected, TemplateConditionExpression.TryEvaluate(OptionSetNameRequired, vars));
    }

    [Theory]
    [InlineData("(IsSystemEntity == true")]
    [InlineData("IsSystemEntity ==")]
    [InlineData("Kind == \"Lookup")]
    [InlineData("Kind == \"Lookup\" Kind")]
    [InlineData("Count > 1")]
    public void Unparsable_ReturnsNull(string expression)
    {
        Assert.Null(TemplateConditionExpression.TryEvaluate(expression, Vars));
    }
}
