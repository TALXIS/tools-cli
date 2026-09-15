using System;
using System.Collections.Generic;
using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.TemplateEngine;

public class TemplateParameterValidatorTests
{
    private static readonly string[] Known = { "TextFormat", "MaxLength", "DisplayName", "type", "language", "name" };
    private static readonly string[] Suggest = { "TextFormat", "MaxLength", "DisplayName" };

    private static ISet<string> KnownSet() => new HashSet<string>(Known, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void KnownParameter_NoError()
    {
        var errors = TemplateParameterValidator.FindUnknownParameters(
            new[] { "TextFormat" }, KnownSet(), Suggest, "pp-entity-attribute");
        Assert.Empty(errors);
    }

    [Fact]
    public void KnownParameter_CaseInsensitive_NoError()
    {
        var errors = TemplateParameterValidator.FindUnknownParameters(
            new[] { "textformat" }, KnownSet(), Suggest, "pp-entity-attribute");
        Assert.Empty(errors);
    }

    [Fact]
    public void SystemParameter_NoError()
    {
        var errors = TemplateParameterValidator.FindUnknownParameters(
            new[] { "name" }, KnownSet(), Suggest, "pp-entity-attribute");
        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownParameter_ProducesError_WithTemplateName()
    {
        var errors = TemplateParameterValidator.FindUnknownParameters(
            new[] { "Xyzzy" }, KnownSet(), Suggest, "pp-entity-attribute");

        var msg = Assert.Single(errors);
        Assert.Contains("Unknown parameter 'Xyzzy'", msg);
        Assert.Contains("pp-entity-attribute", msg);
    }

    [Fact]
    public void UnknownParameter_Typo_SuggestsClosest()
    {
        var errors = TemplateParameterValidator.FindUnknownParameters(
            new[] { "TextFromat" }, KnownSet(), Suggest, "pp-entity-attribute"); // 'rm' transposed

        var msg = Assert.Single(errors);
        Assert.Contains("Did you mean 'TextFormat'?", msg);
    }

    [Fact]
    public void UnknownParameter_FarFromAll_NoSuggestion()
    {
        var errors = TemplateParameterValidator.FindUnknownParameters(
            new[] { "CompletelyUnrelatedThing" }, KnownSet(), Suggest, "pp-entity-attribute");

        var msg = Assert.Single(errors);
        Assert.DoesNotContain("Did you mean", msg);
    }
}
