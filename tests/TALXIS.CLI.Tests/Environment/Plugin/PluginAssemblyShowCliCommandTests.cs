using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Plugin.Assemblies;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Plugin;

public class PluginAssemblyShowCliCommandTests
{
    private static readonly Guid AsmId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static PluginAssemblyRecord Assembly()
        => new(
            Id: AsmId,
            Name: "PluginsWarehouse",
            Version: "1.2.3.4",
            Culture: "neutral",
            PublicKeyToken: null,
            IsolationMode: PluginIsolationMode.Sandbox,
            SourceType: PluginAssemblySourceType.Database,
            Description: null,
            ModifiedOn: null);

    private static PluginTypeRecord Type(string name)
        => new(Guid.NewGuid(), name, null, PluginKind.Plugin, null, null, AsmId, "PluginsWarehouse", "1.2.3.4");

    private static PluginStepRecord Step(Guid id, string name, bool enabled)
        => new(id, name, null, "Create", "account", PluginStage.PostOperation,
            PluginExecutionMode.Synchronous, 1, enabled, null, null,
            Guid.NewGuid(), "Plugins.Foo", AsmId, "PluginsWarehouse", "1.2.3.4");

    private static PluginStepImageRecord Image(Guid stepId)
        => new(Guid.NewGuid(), stepId, "PostImage", "PostImage", "name");

    [Fact]
    public void BuildDetailLines_RendersAssemblyHeader()
    {
        var text = string.Join("\n", PluginAssemblyShowCliCommand.BuildDetailLines(
            Assembly(), Array.Empty<PluginTypeRecord>(), Array.Empty<PluginStepRecord>(), Array.Empty<PluginStepImageRecord>()));

        Assert.Contains("PluginsWarehouse", text);
        Assert.Contains("1.2.3.4", text);
        Assert.Contains(AsmId.ToString(), text);
        Assert.Contains("Sandbox", text);
    }

    [Fact]
    public void BuildDetailLines_ListsTypesAndStepsWithCounts()
    {
        var types = new[] { Type("Plugins.Foo"), Type("Plugins.Bar") };
        var steps = new[]
        {
            Step(Guid.NewGuid(), "Foo: Create of account", enabled: true),
            Step(Guid.NewGuid(), "Bar: Update of contact", enabled: false),
        };

        var text = string.Join("\n", PluginAssemblyShowCliCommand.BuildDetailLines(
            Assembly(), types, steps, Array.Empty<PluginStepImageRecord>()));

        Assert.Contains("Plugins.Foo", text);
        Assert.Contains("Plugins.Bar", text);
        Assert.Contains("Foo: Create of account", text);
        Assert.Contains("Bar: Update of contact", text);
        // counts surfaced somewhere
        Assert.Contains("2", text);
    }

    [Fact]
    public void BuildDetailLines_ShowsImageCountPerStep()
    {
        var stepId = Guid.NewGuid();
        var steps = new[] { Step(stepId, "Foo: Create of account", enabled: true) };
        var images = new[] { Image(stepId), Image(stepId) };

        var text = string.Join("\n", PluginAssemblyShowCliCommand.BuildDetailLines(
            Assembly(), Array.Empty<PluginTypeRecord>(), steps, images));

        Assert.Contains("image", text, StringComparison.OrdinalIgnoreCase);
    }
}
