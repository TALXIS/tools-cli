using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

public class GuideReasoningEngineTests
{
    private readonly GuideReasoningEngine _engine;

    public GuideReasoningEngineTests()
    {
        _engine = new GuideReasoningEngine();
        _engine.LoadSkills();
    }

    [Fact]
    public void LoadSkills_LoadsInternalSkillFilesFromEmbeddedResources()
    {
        // The Skills/Internal directory contains .md files that should be loaded as embedded resources
        Assert.True(_engine.Count > 0, "Expected at least one internal skill to be loaded");
    }

    [Fact]
    public void GetSkillsContext_ForGuideWorkspace_IncludesLocalFirstPhilosophy()
    {
        var context = _engine.GetSkillsContext("guide_workspace");

        Assert.False(string.IsNullOrEmpty(context), "guide_workspace should return non-empty skills context");
        // guide_workspace is mapped to ["local-first-philosophy", "schema-workflow"]
        Assert.Contains("INTERNAL DEVELOPMENT GUIDELINES", context);
    }

    [Fact]
    public void GetSkillsContext_ForGuideConfig_ReturnsEmpty()
    {
        // guide_config is mapped to an empty array []
        var context = _engine.GetSkillsContext("guide_config");

        Assert.Equal(string.Empty, context);
    }

    [Fact]
    public void GetSkillsContext_ForUnknownGuide_ReturnsEmpty()
    {
        var context = _engine.GetSkillsContext("guide_nonexistent");

        Assert.Equal(string.Empty, context);
    }

    [Fact]
    public void Count_ReflectsLoadedSkills()
    {
        // Skills/Internal contains .md files loaded as embedded resources
        Assert.True(_engine.Count >= 6, $"Expected at least 6 internal skills, found {_engine.Count}");
    }
}
