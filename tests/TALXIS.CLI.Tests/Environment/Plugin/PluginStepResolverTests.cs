using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Plugin.Steps;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Plugin;

public class PluginStepResolverTests
{
    private static PluginStepRecord Step(Guid id, string name)
        => new(
            Id: id,
            Name: name,
            Description: null,
            Message: "Create",
            PrimaryEntity: "account",
            Stage: PluginStage.PostOperation,
            Mode: PluginExecutionMode.Synchronous,
            Rank: 1,
            Enabled: true,
            FilteringAttributes: null,
            Configuration: null,
            PluginTypeId: Guid.NewGuid(),
            PluginTypeName: "Some.Plugin",
            AssemblyId: Guid.NewGuid(),
            AssemblyName: "Asm",
            AssemblyVersion: "1.0.0.0");

    [Fact]
    public void Resolve_ByGuid_MatchesById()
    {
        var id = Guid.NewGuid();
        var rows = new[] { Step(id, "Alpha"), Step(Guid.NewGuid(), "Beta") };

        var result = PluginStepResolver.Resolve(rows, id.ToString());

        Assert.Null(result.Error);
        Assert.NotNull(result.Step);
        Assert.Equal(id, result.Step!.Id);
    }

    [Fact]
    public void Resolve_ByGuid_NotFound_ReturnsError()
    {
        var rows = new[] { Step(Guid.NewGuid(), "Alpha") };

        var result = PluginStepResolver.Resolve(rows, Guid.NewGuid().ToString());

        Assert.Null(result.Step);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Resolve_ByExactName_IsCaseInsensitive()
    {
        var id = Guid.NewGuid();
        var rows = new[] { Step(id, "Plugins.Foo: Create of account"), Step(Guid.NewGuid(), "Other") };

        var result = PluginStepResolver.Resolve(rows, "plugins.foo: create of account");

        Assert.Null(result.Error);
        Assert.Equal(id, result.Step!.Id);
    }

    [Fact]
    public void Resolve_BySubstring_WhenNoExactMatch()
    {
        var id = Guid.NewGuid();
        var rows = new[] { Step(id, "Plugins.Foo: Create of account"), Step(Guid.NewGuid(), "Bar: Update") };

        var result = PluginStepResolver.Resolve(rows, "Foo");

        Assert.Null(result.Error);
        Assert.Equal(id, result.Step!.Id);
    }

    [Fact]
    public void Resolve_ExactMatch_WinsOverSubstring()
    {
        var exactId = Guid.NewGuid();
        var rows = new[]
        {
            Step(exactId, "Create"),
            Step(Guid.NewGuid(), "Create of account"),
        };

        var result = PluginStepResolver.Resolve(rows, "Create");

        Assert.Null(result.Error);
        Assert.Equal(exactId, result.Step!.Id);
    }

    [Fact]
    public void Resolve_AmbiguousSubstring_ReturnsError()
    {
        var rows = new[]
        {
            Step(Guid.NewGuid(), "Foo: Create"),
            Step(Guid.NewGuid(), "Foo: Update"),
        };

        var result = PluginStepResolver.Resolve(rows, "Foo");

        Assert.Null(result.Step);
        Assert.NotNull(result.Error);
        Assert.Contains("ambiguous", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_NotFound_ReturnsError()
    {
        var rows = new[] { Step(Guid.NewGuid(), "Alpha") };

        var result = PluginStepResolver.Resolve(rows, "nothing-here");

        Assert.Null(result.Step);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Resolve_EmptyToken_ReturnsError()
    {
        var rows = new[] { Step(Guid.NewGuid(), "Alpha") };

        var result = PluginStepResolver.Resolve(rows, "   ");

        Assert.Null(result.Step);
        Assert.NotNull(result.Error);
    }
}
