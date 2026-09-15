using System.IO;
using TALXIS.CLI.Features.Workspace;
using Xunit;

namespace TALXIS.CLI.Tests.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceFileFilter"/>. Cover the two rule
/// sources independently (defaults vs. .gitignore) plus the opt-out paths.
/// </summary>
public class WorkspaceFileFilterTests : IDisposable
{
    private readonly string _root;

    public WorkspaceFileFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "txc-tests", "filter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("src/Modules/Core/Apps/Home.Presentation/TS/node_modules/excellib/lib/xlsx/workbook.xml", true)]
    [InlineData("node_modules/foo.xml", true)]
    [InlineData("src/bin/Debug/net10.0/foo.dll", true)]
    [InlineData("src/obj/project.assets.json", true)]
    [InlineData("src/Solutions/MySolution/Entities/account.xml", false)]
    [InlineData("src/MyComponent/code.ts", false)]
    public void DefaultIgnoredDirectories_AreFiltered(string relative, bool ignored)
    {
        var filter = new WorkspaceFileFilter(_root, applyDefaults: true, readGitignore: false);

        var absolute = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(ignored, filter.IsIgnored(absolute));
    }

    [Fact]
    public void NoDefaults_NodeModulesPasses()
    {
        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: false);

        var absolute = Path.Combine(_root, "src", "node_modules", "foo.xml");
        Assert.False(filter.IsIgnored(absolute));
    }

    [Fact]
    public void Gitignore_DirectoryPattern_IsHonored()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "exports/\n");

        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: true);
        Assert.Equal(1, filter.GitignorePatternCount);

        Assert.True(filter.IsIgnored(Path.Combine(_root, "exports", "out.json")));
        Assert.True(filter.IsIgnored(Path.Combine(_root, "src", "exports", "deep.xml")));
        Assert.False(filter.IsIgnored(Path.Combine(_root, "src", "Solutions", "x.xml")));
    }

    [Fact]
    public void Gitignore_AnchoredPattern_OnlyAtRoot()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "/exports\n");

        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: true);

        Assert.True(filter.IsIgnored(Path.Combine(_root, "exports", "x.xml")));
        Assert.False(filter.IsIgnored(Path.Combine(_root, "src", "exports", "x.xml")));
    }

    [Fact]
    public void Gitignore_GlobExtensions_AreMatched()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "*.log\n");

        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: true);

        Assert.True(filter.IsIgnored(Path.Combine(_root, "build.log")));
        Assert.True(filter.IsIgnored(Path.Combine(_root, "src", "trace.log")));
        Assert.False(filter.IsIgnored(Path.Combine(_root, "src", "trace.xml")));
    }

    [Fact]
    public void Gitignore_CommentsAndBlanksAreSkipped()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"),
            "# this is a comment\n\nbin/\n  # padded comment too\n");

        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: true);
        Assert.Equal(1, filter.GitignorePatternCount);
    }

    [Fact]
    public void Gitignore_NegationsAreIgnored()
    {
        // We deliberately do not implement `!pattern` semantics. Negations
        // should be parsed away without affecting other rules.
        File.WriteAllText(Path.Combine(_root, ".gitignore"),
            "node_modules/\n!node_modules/keep.xml\n");

        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: true);

        Assert.True(filter.IsIgnored(Path.Combine(_root, "node_modules", "keep.xml")));
        Assert.Equal(1, filter.GitignorePatternCount);
    }

    [Fact]
    public void NoGitignoreFile_NoRulesLoaded()
    {
        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: true);
        Assert.Equal(0, filter.GitignorePatternCount);
    }

    [Fact]
    public void Defaults_AndGitignore_Compose()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "custom-junk/\n");

        var filter = new WorkspaceFileFilter(_root, applyDefaults: true, readGitignore: true);

        Assert.True(filter.IsIgnored(Path.Combine(_root, "node_modules", "x.xml")));   // default
        Assert.True(filter.IsIgnored(Path.Combine(_root, "custom-junk", "x.xml")));    // gitignore
        Assert.False(filter.IsIgnored(Path.Combine(_root, "src", "x.xml")));
    }

    [Fact]
    public void NodeProject_TsConfigsAreSkipped()
    {
        var tsDir = Path.Combine(_root, "TS");
        Directory.CreateDirectory(tsDir);
        File.WriteAllText(Path.Combine(tsDir, "package.json"), "{}");

        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: false);

        Assert.True(filter.IsIgnored(Path.Combine(tsDir, "package.json")));
        Assert.True(filter.IsIgnored(Path.Combine(tsDir, "package-lock.json")));
        Assert.True(filter.IsIgnored(Path.Combine(tsDir, "tsconfig.json")));
        Assert.True(filter.IsIgnored(Path.Combine(tsDir, "src", "deep", "config.json")));
    }

    [Fact]
    public void NodeProject_SiblingDirectoriesArentSkipped()
    {
        var tsDir = Path.Combine(_root, "TS");
        Directory.CreateDirectory(tsDir);
        File.WriteAllText(Path.Combine(tsDir, "package.json"), "{}");

        var solutionsDir = Path.Combine(_root, "Solutions");
        Directory.CreateDirectory(solutionsDir);

        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: false);

        Assert.False(filter.IsIgnored(Path.Combine(solutionsDir, "MySolution", "Entities", "account.xml")));
    }

    [Fact]
    public void NodeProject_SkipDisabled_TsConfigsValidated()
    {
        var tsDir = Path.Combine(_root, "TS");
        Directory.CreateDirectory(tsDir);
        File.WriteAllText(Path.Combine(tsDir, "package.json"), "{}");

        var filter = new WorkspaceFileFilter(_root, applyDefaults: false, readGitignore: false, skipNodeProjects: false);

        Assert.False(filter.IsIgnored(Path.Combine(tsDir, "tsconfig.json")));
    }
}
