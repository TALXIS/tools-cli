using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Plugin.Steps;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Plugin;

public class PluginStepEnableAllCliCommandTests
{
    private static PluginStepRecord Step(Guid id, bool enabled)
        => new(
            Id: id,
            Name: "Step",
            Description: null,
            Message: "Create",
            PrimaryEntity: "account",
            Stage: PluginStage.PostOperation,
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

    [Fact]
    public void SelectDisabledStepIds_ReturnsOnlyDisabled()
    {
        var disabled1 = Guid.NewGuid();
        var disabled2 = Guid.NewGuid();
        var rows = new[]
        {
            Step(Guid.NewGuid(), enabled: true),
            Step(disabled1, enabled: false),
            Step(disabled2, enabled: false),
        };

        var ids = PluginStepEnableAllCliCommand.SelectDisabledStepIds(rows);

        Assert.Equal(new[] { disabled1, disabled2 }.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public void SelectDisabledStepIds_AllEnabled_ReturnsEmpty()
    {
        var rows = new[] { Step(Guid.NewGuid(), enabled: true) };

        var ids = PluginStepEnableAllCliCommand.SelectDisabledStepIds(rows);

        Assert.Empty(ids);
    }
}
