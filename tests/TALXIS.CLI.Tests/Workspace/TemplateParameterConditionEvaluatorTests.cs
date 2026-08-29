using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.Workspace;

/// <summary>
/// Covers isEnabled-condition filtering for the parameter listing: a parameter whose
/// condition references the chosen AttributeType should drop out when it doesn't match,
/// while unconditional parameters always stay.
/// </summary>
public class TemplateParameterConditionEvaluatorTests
{
    // (name, isEnabledCondition, defaultValue) — mirrors what pp-entity-attribute defines.
    private static readonly List<(string, string?, string?)> Params = new()
    {
        ("AttributeType", null, null),                                                   // unconditional
        ("TextFormat", "(AttributeType == \"Text\")", "text"),
        ("WholeNumberFormat", "(AttributeType == \"WholeNumber\")", "none"),
        ("DecimalPrecision", "(AttributeType == \"Decimal\" || AttributeType == \"Float\" || AttributeType == \"Money\")", "2"),
        ("LookupTarget", "(AttributeType == \"Lookup\") || (AttributeType == \"Customer\")", null),
    };

    private static IReadOnlyList<string> Select(params (string k, string v)[] provided)
        => TemplateParameterConditionEvaluator.SelectEnabled(
            Params,
            provided.ToDictionary(p => p.k, p => p.v, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Text_KeepsTextFormat_DropsOtherTypeSpecific()
    {
        var enabled = Select(("AttributeType", "Text"));

        Assert.Contains("AttributeType", enabled);   // unconditional always kept
        Assert.Contains("TextFormat", enabled);
        Assert.DoesNotContain("WholeNumberFormat", enabled);
        Assert.DoesNotContain("DecimalPrecision", enabled);
        Assert.DoesNotContain("LookupTarget", enabled);
    }

    [Fact]
    public void Decimal_KeepsDecimalPrecision_ViaOrCondition()
    {
        var enabled = Select(("AttributeType", "Float"));

        Assert.Contains("DecimalPrecision", enabled);
        Assert.DoesNotContain("TextFormat", enabled);
    }

    [Fact]
    public void Lookup_KeepsLookupTarget()
    {
        var enabled = Select(("AttributeType", "Lookup"));

        Assert.Contains("LookupTarget", enabled);
        Assert.DoesNotContain("TextFormat", enabled);
    }

    [Fact]
    public void NoProvidedValues_StillEvaluates_UnconditionalAlwaysKept()
    {
        // With no AttributeType supplied, defaults are empty so type-specific conditions are
        // false, but unconditional parameters remain.
        var enabled = Select();

        Assert.Contains("AttributeType", enabled);
    }
}
