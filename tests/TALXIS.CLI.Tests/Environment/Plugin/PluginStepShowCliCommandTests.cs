using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Plugin.Steps;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Plugin;

public class PluginStepShowCliCommandTests
{
    private static PluginStepRecord Step(
        bool enabled = false,
        string? filtering = "name,statecode",
        string? configuration = "secure-config")
        => new(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name: "Plugins.Foo: Create of account",
            Description: "Demo step",
            Message: "Create",
            PrimaryEntity: "account",
            Stage: PluginStage.PostOperation,
            Mode: PluginExecutionMode.Synchronous,
            Rank: 5,
            Enabled: enabled,
            FilteringAttributes: filtering,
            Configuration: configuration,
            PluginTypeId: Guid.NewGuid(),
            PluginTypeName: "Plugins.Foo",
            AssemblyId: Guid.NewGuid(),
            AssemblyName: "PluginsWarehouse",
            AssemblyVersion: "1.2.3.4");

    [Fact]
    public void BuildDetailLines_IncludesCoreFields()
    {
        var text = string.Join("\n", PluginStepShowCliCommand.BuildDetailLines(Step()));

        Assert.Contains("Plugins.Foo: Create of account", text);
        Assert.Contains("11111111-1111-1111-1111-111111111111", text);
        Assert.Contains("Create", text);
        Assert.Contains("account", text);
        Assert.Contains("PostOperation", text);
        Assert.Contains("Sync", text);
        Assert.Contains("5", text);
        Assert.Contains("PluginsWarehouse", text);
        Assert.Contains("Plugins.Foo", text);
    }

    [Fact]
    public void BuildDetailLines_ShowsDisabledState()
    {
        var text = string.Join("\n", PluginStepShowCliCommand.BuildDetailLines(Step(enabled: false)));
        Assert.Contains("Disabled", text);
    }

    [Fact]
    public void BuildDetailLines_ShowsEnabledState()
    {
        var text = string.Join("\n", PluginStepShowCliCommand.BuildDetailLines(Step(enabled: true)));
        Assert.Contains("Enabled", text);
    }

    [Fact]
    public void BuildDetailLines_IncludesFilteringAndConfigWhenPresent()
    {
        var text = string.Join("\n", PluginStepShowCliCommand.BuildDetailLines(Step()));
        Assert.Contains("name,statecode", text);
        Assert.Contains("secure-config", text);
    }

    [Fact]
    public void BuildDetailLines_OmitsFilteringAndConfigWhenAbsent()
    {
        var lines = PluginStepShowCliCommand.BuildDetailLines(Step(filtering: null, configuration: null));
        var text = string.Join("\n", lines);
        Assert.DoesNotContain("Filtering Attributes", text);
        Assert.DoesNotContain("Configuration", text);
    }
}
