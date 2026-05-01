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
    public void Rollback_CleansEmptyDirectories()
    {
        var subDir = Path.Combine(_testDir, "sub", "nested");
        Directory.CreateDirectory(subDir);
        var file = Path.Combine(subDir, "file.xml");

        var tx = new PostActionTransaction();
        tx.TrackFile(file);
        tx.TrackNewDirectory(subDir);
        tx.TrackNewDirectory(Path.Combine(_testDir, "sub"));

        File.WriteAllText(file, "content");
        tx.Rollback();

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(subDir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }
}
