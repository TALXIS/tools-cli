using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Plugin.Steps;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Plugin;

public class PluginStepQueryTests
{
    private static PluginStepRecord Step(
        string name = "Step",
        string? entity = "account",
        PluginStage stage = PluginStage.PostOperation,
        bool enabled = true)
        => new(
            Id: Guid.NewGuid(),
            Name: name,
            Description: null,
            Message: "Create",
            PrimaryEntity: entity,
            Stage: stage,
            Mode: PluginExecutionMode.Synchronous,
            Rank: 1,
            Enabled: enabled,
            FilteringAttributes: null,
            Configuration: null,
            PluginTypeId: Guid.NewGuid(),
            PluginTypeName: "Some.Plugin",
            AssemblyId: Guid.NewGuid(),
            AssemblyName: "Asm",
            AssemblyVersion: "1.0.0.0");

    [Theory]
    [InlineData("pre", new[] { PluginStage.PreValidation, PluginStage.PreOperation })]
    [InlineData("PRE", new[] { PluginStage.PreValidation, PluginStage.PreOperation })]
    [InlineData("post", new[] { PluginStage.PostOperation, PluginStage.PostOperationDeprecated })]
    [InlineData("prevalidation", new[] { PluginStage.PreValidation })]
    [InlineData("preoperation", new[] { PluginStage.PreOperation })]
    [InlineData("postoperation", new[] { PluginStage.PostOperation })]
    public void TryParseStageFilter_MapsKnownValues(string value, PluginStage[] expected)
    {
        var ok = PluginStepQuery.TryParseStageFilter(value, out var stages, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected.OrderBy(s => s), stages.OrderBy(s => s));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseStageFilter_EmptyMeansNoFilter(string? value)
    {
        var ok = PluginStepQuery.TryParseStageFilter(value, out var stages, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Empty(stages);
    }

    [Fact]
    public void TryParseStageFilter_InvalidReturnsError()
    {
        var ok = PluginStepQuery.TryParseStageFilter("middle", out var stages, out var error);

        Assert.False(ok);
        Assert.Empty(stages);
        Assert.NotNull(error);
        Assert.Contains("middle", error);
    }

    [Fact]
    public void Filter_NoCriteria_ReturnsAll()
    {
        var rows = new[] { Step("a"), Step("b") };

        var result = PluginStepQuery.Filter(rows, entityContains: null, stages: null, disabledOnly: false);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Filter_ByEntity_IsCaseInsensitiveSubstring()
    {
        var rows = new[]
        {
            Step("a", entity: "account"),
            Step("b", entity: "contact"),
            Step("c", entity: null),
        };

        var result = PluginStepQuery.Filter(rows, entityContains: "ACC", stages: null, disabledOnly: false);

        Assert.Single(result);
        Assert.Equal("a", result[0].Name);
    }

    [Fact]
    public void Filter_ByStage_KeepsOnlyMatchingStages()
    {
        var rows = new[]
        {
            Step("pre", stage: PluginStage.PreOperation),
            Step("post", stage: PluginStage.PostOperation),
        };

        var stages = new[] { PluginStage.PreValidation, PluginStage.PreOperation };
        var result = PluginStepQuery.Filter(rows, entityContains: null, stages: stages, disabledOnly: false);

        Assert.Single(result);
        Assert.Equal("pre", result[0].Name);
    }

    [Fact]
    public void Filter_DisabledOnly_DropsEnabled()
    {
        var rows = new[]
        {
            Step("on", enabled: true),
            Step("off", enabled: false),
        };

        var result = PluginStepQuery.Filter(rows, entityContains: null, stages: null, disabledOnly: true);

        Assert.Single(result);
        Assert.Equal("off", result[0].Name);
    }

    [Fact]
    public void Filter_CombinesCriteria()
    {
        var rows = new[]
        {
            Step("keep", entity: "account", stage: PluginStage.PreOperation, enabled: false),
            Step("wrongEntity", entity: "contact", stage: PluginStage.PreOperation, enabled: false),
            Step("wrongStage", entity: "account", stage: PluginStage.PostOperation, enabled: false),
            Step("enabled", entity: "account", stage: PluginStage.PreOperation, enabled: true),
        };

        var stages = new[] { PluginStage.PreValidation, PluginStage.PreOperation };
        var result = PluginStepQuery.Filter(rows, entityContains: "account", stages: stages, disabledOnly: true);

        Assert.Single(result);
        Assert.Equal("keep", result[0].Name);
    }
}
