using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.TemplateEngine;

public class PostActionTransactionTests : IDisposable
{
    private readonly string _testDir;

    public PostActionTransactionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"txc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void Rollback_RestoresModifiedFile()
    {
        var file = Path.Combine(_testDir, "test.xml");
        File.WriteAllText(file, "original");

        var tx = new PostActionTransaction();
        tx.TrackFile(file);

        File.WriteAllText(file, "modified");
        tx.Rollback();

        Assert.Equal("original", File.ReadAllText(file));
    }

    [Fact]
    public void Rollback_DeletesNewlyCreatedFile()
    {
        var file = Path.Combine(_testDir, "new.xml");

        var tx = new PostActionTransaction();
        tx.TrackFile(file);

        File.WriteAllText(file, "content");
        tx.Rollback();

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Commit_PreventsRollback()
    {
        var file = Path.Combine(_testDir, "test.xml");
        File.WriteAllText(file, "original");

        var tx = new PostActionTransaction();
        tx.TrackFile(file);

        File.WriteAllText(file, "modified");
        tx.Commit();
        tx.Rollback();

        Assert.Equal("modified", File.ReadAllText(file));
    }

    [Fact]
    public void TrackFile_OnlyKeepsFirstSnapshot()
    {
        var file = Path.Combine(_testDir, "test.xml");
        File.WriteAllText(file, "v1");

        var tx = new PostActionTransaction();
        tx.TrackFile(file);

        File.WriteAllText(file, "v2");
        tx.TrackFile(file);

        File.WriteAllText(file, "v3");
        tx.Rollback();

        Assert.Equal("v1", File.ReadAllText(file));
    }

    [Fact]
    public void Rollback_RemovesTrackedDirectoryThatDidNotExist()
    {
        var output = Path.Combine(_testDir, "Repro");

        var tx = new PostActionTransaction();
        tx.TrackDirectory(output);

        Directory.CreateDirectory(Path.Combine(output, "Other"));
        File.WriteAllText(Path.Combine(output, "Repro.csproj"), "project");
        File.WriteAllText(Path.Combine(output, "Other", "Solution.xml"), "solution");
        tx.Rollback();

        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void Rollback_KeepsWhatExistedInTrackedDirectory()
    {
        var existingDir = Path.Combine(_testDir, "existing");
        Directory.CreateDirectory(existingDir);
        var existingFile = Path.Combine(existingDir, "keep.xml");
        File.WriteAllText(existingFile, "original");

        var tx = new PostActionTransaction();
        tx.TrackDirectory(_testDir);

        File.WriteAllText(existingFile, "modified");
        File.WriteAllText(Path.Combine(existingDir, "new.xml"), "new");
        Directory.CreateDirectory(Path.Combine(_testDir, "created", "nested"));
        File.WriteAllText(Path.Combine(_testDir, "created", "nested", "file.xml"), "new");
        tx.Rollback();

        Assert.Equal("original", File.ReadAllText(existingFile));
        Assert.False(File.Exists(Path.Combine(existingDir, "new.xml")));
        Assert.False(Directory.Exists(Path.Combine(_testDir, "created")));
        Assert.True(Directory.Exists(_testDir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }
}
