using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.TemplateEngine;

public class PostActionDispatcherTests
{
    private static readonly Guid AddReferenceActionId = new("B17581D1-C5C9-4489-8F0A-004BE667B814");

    [Fact]
    public void OptionalFailingAction_DoesNotFailTheRun()
    {
        var dispatcher = new PostActionDispatcher(null!, (_, _) => true);

        var (result, failed) = dispatcher.RunPostActions(new[] { new FakePostAction(continueOnError: true) }, ScriptPermission.Yes);

        Assert.Equal(PostActionResult.Success, result);
        Assert.Empty(failed);
    }

    [Fact]
    public void RequiredFailingAction_FailsTheRunWithItsError()
    {
        var dispatcher = new PostActionDispatcher(null!, (_, _) => true);
        var action = new FakePostAction(continueOnError: false);

        var (result, failed) = dispatcher.RunPostActions(new[] { action }, ScriptPermission.Yes);

        Assert.True((result & PostActionResult.Failure) != 0);
        Assert.Single(failed);
        Assert.Contains("missing required arguments", dispatcher.FailedActionErrors[AddReferenceActionId]);
    }

    private sealed class FakePostAction(bool continueOnError) : IPostAction
    {
        public string? Description => "Add reference";
        public Guid ActionId => AddReferenceActionId;
        public bool ContinueOnError => continueOnError;
        public IReadOnlyDictionary<string, string> Args { get; } = new Dictionary<string, string>();
        public string? ManualInstructions => null;
    }
}
