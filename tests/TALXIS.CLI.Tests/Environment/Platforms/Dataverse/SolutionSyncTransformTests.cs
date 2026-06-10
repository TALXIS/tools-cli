using System.Xml.Linq;
using TALXIS.CLI.Core.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionSyncTransformTests : IDisposable
{
    private readonly string _root;

    public SolutionSyncTransformTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "txc_sync_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteAssembly(string folderName, string fileBaseName, string fullName, string fileNameElement)
    {
        var dir = Path.Combine(_root, "PluginAssemblies", folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileBaseName + ".dll"), "binary");
        var dataXml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <PluginAssembly FullName="{fullName}" PluginAssemblyId="d0333984-669b-4927-81ad-cadbf05ecb0c">
              <SourceType>0</SourceType>
              <FileName>{fileNameElement}</FileName>
            </PluginAssembly>
            """;
        File.WriteAllText(Path.Combine(dir, fileBaseName + ".dll.data.xml"), dataXml);
        return dir;
    }

    [Fact]
    public void Normalize_FlattensFolderAndRewritesFileName()
    {
        WriteAssembly(
            "MyPlugin-38E8D392-49D6-4DE7-9FF7-F1338E8DD6EE",
            "MyPlugin",
            "Acme.MyPlugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            "/PluginAssemblies/MyPlugin-38E8D392-49D6-4DE7-9FF7-F1338E8DD6EE/MyPlugin.dll");

        var normalized = SolutionSyncTransform.NormalizePluginAssemblyPaths(_root);

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Equal(new[] { "MyPlugin.dll" }, normalized);
        Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll.data.xml")));
        Assert.Empty(Directory.GetDirectories(pluginsDir));

        var doc = XDocument.Load(Path.Combine(pluginsDir, "MyPlugin.dll.data.xml"));
        Assert.Equal("/PluginAssemblies/MyPlugin.dll", doc.Descendants("FileName").Single().Value);
    }

    [Fact]
    public void Normalize_IsIdempotent_WhenAlreadyFlat()
    {
        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Directory.CreateDirectory(pluginsDir);
        File.WriteAllText(Path.Combine(pluginsDir, "Flat.dll"), "binary");
        File.WriteAllText(Path.Combine(pluginsDir, "Flat.dll.data.xml"), "<PluginAssembly><FileName>/PluginAssemblies/Flat.dll</FileName></PluginAssembly>");

        var normalized = SolutionSyncTransform.NormalizePluginAssemblyPaths(_root);

        Assert.Empty(normalized);
        Assert.True(File.Exists(Path.Combine(pluginsDir, "Flat.dll")));
    }

    [Fact]
    public void Normalize_NoOp_WhenNoPluginAssembliesFolder()
    {
        var normalized = SolutionSyncTransform.NormalizePluginAssemblyPaths(_root);
        Assert.Empty(normalized);
    }

    [Fact]
    public void Exclude_DeletesDll_KeepsDataXml_ForExactMatch()
    {
        WriteAssembly("X", "MyPlugin", "MyPlugin, Version=1.0.0.0", "/PluginAssemblies/MyPlugin.dll");
        SolutionSyncTransform.NormalizePluginAssemblyPaths(_root);

        var excluded = SolutionSyncTransform.ExcludeProjectReferenceBinaries(_root, new[] { "MyPlugin" });

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Equal(new[] { "MyPlugin.dll" }, excluded);
        Assert.False(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll.data.xml")));
    }

    [Fact]
    public void Exclude_MatchesDottedNamespaceExtension()
    {
        WriteAssembly("X", "MoveOrder.Logic", "Acme.Apps.MoveOrder.Logic, Version=1.0.0.0", "/PluginAssemblies/MoveOrder.Logic.dll");
        SolutionSyncTransform.NormalizePluginAssemblyPaths(_root);

        var excluded = SolutionSyncTransform.ExcludeProjectReferenceBinaries(_root, new[] { "MoveOrder.Logic" });

        Assert.Equal(new[] { "MoveOrder.Logic.dll" }, excluded);
    }

    [Fact]
    public void Exclude_KeepsBinary_WhenNotReferenced()
    {
        WriteAssembly("X", "ThirdParty", "ThirdParty, Version=1.0.0.0", "/PluginAssemblies/ThirdParty.dll");
        SolutionSyncTransform.NormalizePluginAssemblyPaths(_root);

        var excluded = SolutionSyncTransform.ExcludeProjectReferenceBinaries(_root, new[] { "MyPlugin" });

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Empty(excluded);
        Assert.True(File.Exists(Path.Combine(pluginsDir, "ThirdParty.dll")));
    }

    [Fact]
    public void Exclude_NoOp_WhenNoReferences()
    {
        WriteAssembly("X", "MyPlugin", "MyPlugin, Version=1.0.0.0", "/PluginAssemblies/MyPlugin.dll");
        SolutionSyncTransform.NormalizePluginAssemblyPaths(_root);

        var excluded = SolutionSyncTransform.ExcludeProjectReferenceBinaries(_root, Array.Empty<string>());

        Assert.Empty(excluded);
        Assert.True(File.Exists(Path.Combine(_root, "PluginAssemblies", "MyPlugin.dll")));
    }
}
