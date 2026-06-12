using TALXIS.CLI.Core.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionSyncMergeTests : IDisposable
{
    private readonly string _from;
    private readonly string _into;

    public SolutionSyncMergeTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "txc_merge_test_" + Guid.NewGuid().ToString("N"));
        _from = Path.Combine(baseDir, "from");
        _into = Path.Combine(baseDir, "into");
        Directory.CreateDirectory(_from);
        Directory.CreateDirectory(_into);
    }

    public void Dispose()
    {
        var baseDir = Path.GetDirectoryName(_from);
        if (baseDir is not null && Directory.Exists(baseDir))
            Directory.Delete(baseDir, recursive: true);
    }

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Merge_NeverDeletesProjectFileOrBuildOutput()
    {
        Write(_into, "Solutions.Logic.csproj", "<Project/>");
        Write(_into, "bin/Debug/whatever.dll", "binary");
        Write(_into, "obj/project.assets.json", "{}");
        Write(_into, "map.xml", "<Mapping/>");
        Write(_into, "Other/Solution.xml", "<old/>");

        Write(_from, "Other/Solution.xml", "<new/>");
        Write(_from, "Entities/account/Entity.xml", "<entity/>");

        SolutionSyncMerge.Merge(_from, _into);

        Assert.True(File.Exists(Path.Combine(_into, "Solutions.Logic.csproj")));
        Assert.True(File.Exists(Path.Combine(_into, "bin", "Debug", "whatever.dll")));
        Assert.True(File.Exists(Path.Combine(_into, "obj", "project.assets.json")));
        Assert.True(File.Exists(Path.Combine(_into, "map.xml")));
        Assert.Equal("<new/>", File.ReadAllText(Path.Combine(_into, "Other", "Solution.xml")));
        Assert.True(File.Exists(Path.Combine(_into, "Entities", "account", "Entity.xml")));
    }

    [Fact]
    public void Merge_RemovesStaleFilesWithinComponentFolders()
    {
        Write(_into, "Entities/account/Entity.xml", "<a/>");
        Write(_into, "Entities/contact/Entity.xml", "<c/>");
        Write(_from, "Entities/account/Entity.xml", "<a/>");

        var removed = SolutionSyncMerge.Merge(_from, _into);

        Assert.True(File.Exists(Path.Combine(_into, "Entities", "account", "Entity.xml")));
        Assert.False(Directory.Exists(Path.Combine(_into, "Entities", "contact")));
        Assert.Contains(removed, r => r.Replace('\\', '/') == "Entities/contact/Entity.xml");
    }

    [Fact]
    public void Merge_LeavesUntouchedComponentFoldersAbsentFromExport()
    {
        Write(_into, "WebResources/udpp_main.js.data.xml", "<w/>");
        Write(_from, "Other/Solution.xml", "<s/>");

        var removed = SolutionSyncMerge.Merge(_from, _into);

        Assert.True(File.Exists(Path.Combine(_into, "WebResources", "udpp_main.js.data.xml")));
        Assert.Empty(removed);
    }
}
