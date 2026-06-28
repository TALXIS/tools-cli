using System.Xml.Linq;
using TALXIS.CLI.Core.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionPullTransformTests : IDisposable
{
    private readonly string _root;

    // A non-existent path simulates a first-sync scenario: no local convention established yet.
    // All assemblies are treated as new and default to the flat TALXIS SDK layout.
    private string NoLocalConvention => Path.Combine(Path.GetTempPath(), "no_dest_" + Guid.NewGuid().ToString("N"));

    public SolutionPullTransformTests()
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

    // ──────────────────────────────────────────────────────────────────────────────
    // RestoreLocalFileNameConventions — first-sync / no local convention (flat default)
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Restore_NewAssembly_DefaultsToFlatLayoutAndRewritesFileName()
    {
        WriteAssembly(
            "MyPlugin-38E8D392-49D6-4DE7-9FF7-F1338E8DD6EE",
            "MyPlugin",
            "Acme.MyPlugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            "/PluginAssemblies/MyPlugin-38E8D392-49D6-4DE7-9FF7-F1338E8DD6EE/MyPlugin.dll");

        var restored = SolutionPullTransform.RestoreLocalFileNameConventions(_root, NoLocalConvention);

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Equal(new[] { "Acme.MyPlugin" }, restored);
        Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll.data.xml")));
        Assert.Empty(Directory.GetDirectories(pluginsDir));

        var doc = XDocument.Load(Path.Combine(pluginsDir, "MyPlugin.dll.data.xml"));
        Assert.Equal("/PluginAssemblies/MyPlugin.dll", doc.Descendants("FileName").Single().Value);
    }

    [Fact]
    public void Restore_NoOp_WhenFilesAlreadyFlat()
    {
        // Files directly in PluginAssemblies/ (no sub-directories) → nothing to move.
        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Directory.CreateDirectory(pluginsDir);
        File.WriteAllText(Path.Combine(pluginsDir, "Flat.dll"), "binary");
        File.WriteAllText(Path.Combine(pluginsDir, "Flat.dll.data.xml"),
            "<PluginAssembly FullName=\"Flat, Version=1.0.0.0\"><FileName>/PluginAssemblies/Flat.dll</FileName></PluginAssembly>");

        var restored = SolutionPullTransform.RestoreLocalFileNameConventions(_root, NoLocalConvention);

        Assert.Empty(restored);
        Assert.True(File.Exists(Path.Combine(pluginsDir, "Flat.dll")));
    }

    [Fact]
    public void Restore_NoOp_WhenNoPluginAssembliesFolder()
    {
        var restored = SolutionPullTransform.RestoreLocalFileNameConventions(_root, NoLocalConvention);
        Assert.Empty(restored);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // RestoreLocalFileNameConventions — existing local convention respected
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Restore_PreservesLocalNestedConvention()
    {
        // Staging: Dataverse-exported nested folder (Name-GUID)
        WriteAssembly(
            "MyPlugin-AAAABBBB",
            "MyPlugin",
            "Acme.MyPlugin, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null",
            "/PluginAssemblies/MyPlugin-AAAABBBB/MyPlugin.dll");

        // Local destination: repo already uses a different nested folder name (from prior sync)
        var destRoot = Path.Combine(Path.GetTempPath(), "dest_" + Guid.NewGuid().ToString("N"));
        var destPlugins = Path.Combine(destRoot, "PluginAssemblies", "MyPlugin-CCCCDDDD");
        Directory.CreateDirectory(destPlugins);
        File.WriteAllText(Path.Combine(destPlugins, "MyPlugin.dll.data.xml"),
            "<PluginAssembly FullName=\"Acme.MyPlugin, Version=1.0.0.0\">" +
            "<FileName>/PluginAssemblies/MyPlugin-CCCCDDDD/MyPlugin.dll</FileName></PluginAssembly>");

        try
        {
            var restored = SolutionPullTransform.RestoreLocalFileNameConventions(_root, destRoot);

            var pluginsDir = Path.Combine(_root, "PluginAssemblies");
            // File should land in MyPlugin-CCCCDDDD (matching local convention), NOT flat.
            Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin-CCCCDDDD", "MyPlugin.dll")));
            Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin-CCCCDDDD", "MyPlugin.dll.data.xml")));
            Assert.False(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll.data.xml")));

            // <FileName> must NOT have been rewritten — local value is preserved.
            var doc = XDocument.Load(Path.Combine(pluginsDir, "MyPlugin-CCCCDDDD", "MyPlugin.dll.data.xml"));
            Assert.Equal(
                "/PluginAssemblies/MyPlugin-CCCCDDDD/MyPlugin.dll",
                doc.Descendants("FileName").Single().Value);
        }
        finally
        {
            Directory.Delete(destRoot, recursive: true);
        }
    }

    [Fact]
    public void Restore_PreservesLocalFlatConvention()
    {
        // Staging: nested (as Dataverse exports it)
        WriteAssembly(
            "FlatPlugin-AAAABBBB",
            "FlatPlugin",
            "FlatPlugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            "/PluginAssemblies/FlatPlugin-AAAABBBB/FlatPlugin.dll");

        // Local destination: flat (TALXIS SDK style)
        var destRoot = Path.Combine(Path.GetTempPath(), "dest_" + Guid.NewGuid().ToString("N"));
        var destPlugins = Path.Combine(destRoot, "PluginAssemblies");
        Directory.CreateDirectory(destPlugins);
        File.WriteAllText(Path.Combine(destPlugins, "FlatPlugin.dll.data.xml"),
            "<PluginAssembly FullName=\"FlatPlugin, Version=0.9.0.0\">" +
            "<FileName>/PluginAssemblies/FlatPlugin.dll</FileName></PluginAssembly>");

        try
        {
            SolutionPullTransform.RestoreLocalFileNameConventions(_root, destRoot);

            var pluginsDir = Path.Combine(_root, "PluginAssemblies");
            Assert.True(File.Exists(Path.Combine(pluginsDir, "FlatPlugin.dll")));
            Assert.True(File.Exists(Path.Combine(pluginsDir, "FlatPlugin.dll.data.xml")));
            Assert.Empty(Directory.GetDirectories(pluginsDir));

            // <FileName> should still be the local flat value (not rewritten to staging version).
            var doc = XDocument.Load(Path.Combine(pluginsDir, "FlatPlugin.dll.data.xml"));
            Assert.Equal("/PluginAssemblies/FlatPlugin.dll", doc.Descendants("FileName").Single().Value);
        }
        finally
        {
            Directory.Delete(destRoot, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // ExcludeProjectReferenceBinaries
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Exclude_DeletesDll_KeepsDataXml_ForExactMatch()
    {
        WriteAssembly("X", "MyPlugin", "MyPlugin, Version=1.0.0.0", "/PluginAssemblies/MyPlugin.dll");
        SolutionPullTransform.RestoreLocalFileNameConventions(_root, NoLocalConvention);

        var excluded = SolutionPullTransform.ExcludeProjectReferenceBinaries(_root, new[] { "MyPlugin" });

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Equal(new[] { "MyPlugin.dll" }, excluded);
        Assert.False(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll.data.xml")));
    }

    [Fact]
    public void Exclude_MatchesDottedNamespaceExtension()
    {
        WriteAssembly("X", "MoveOrder.Logic", "Acme.Apps.MoveOrder.Logic, Version=1.0.0.0", "/PluginAssemblies/MoveOrder.Logic.dll");
        SolutionPullTransform.RestoreLocalFileNameConventions(_root, NoLocalConvention);

        var excluded = SolutionPullTransform.ExcludeProjectReferenceBinaries(_root, new[] { "MoveOrder.Logic" });

        Assert.Equal(new[] { "MoveOrder.Logic.dll" }, excluded);
    }

    [Fact]
    public void Exclude_DoesNotFalsePositive_OnUnrelatedSuffixMatch()
    {
        // Third-party assembly "Logic" must NOT be deleted just because a referenced project
        // has AssemblyName "Acme.Apps.MoveOrder.Logic" (which ends with ".Logic").
        WriteAssembly("A", "Logic", "Logic, Version=1.0.0.0", "/PluginAssemblies/Logic.dll");
        WriteAssembly("B", "MoveOrder.Logic", "Acme.Apps.MoveOrder.Logic, Version=1.0.0.0", "/PluginAssemblies/MoveOrder.Logic.dll");
        SolutionPullTransform.RestoreLocalFileNameConventions(_root, NoLocalConvention);

        // Only MoveOrder.Logic is referenced; Logic is a third-party assembly.
        var excluded = SolutionPullTransform.ExcludeProjectReferenceBinaries(_root, new[] { "MoveOrder.Logic" });

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Equal(new[] { "MoveOrder.Logic.dll" }, excluded);
        // Third-party "Logic.dll" must survive.
        Assert.True(File.Exists(Path.Combine(pluginsDir, "Logic.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsDir, "Logic.dll.data.xml")));
    }

    [Fact]
    public void Exclude_KeepsBinary_WhenNotReferenced()
    {
        WriteAssembly("X", "ThirdParty", "ThirdParty, Version=1.0.0.0", "/PluginAssemblies/ThirdParty.dll");
        SolutionPullTransform.RestoreLocalFileNameConventions(_root, NoLocalConvention);

        var excluded = SolutionPullTransform.ExcludeProjectReferenceBinaries(_root, new[] { "MyPlugin" });

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Empty(excluded);
        Assert.True(File.Exists(Path.Combine(pluginsDir, "ThirdParty.dll")));
    }

    [Fact]
    public void Exclude_NoOp_WhenNoReferences()
    {
        WriteAssembly("X", "MyPlugin", "MyPlugin, Version=1.0.0.0", "/PluginAssemblies/MyPlugin.dll");
        SolutionPullTransform.RestoreLocalFileNameConventions(_root, NoLocalConvention);

        var excluded = SolutionPullTransform.ExcludeProjectReferenceBinaries(_root, Array.Empty<string>());

        Assert.Empty(excluded);
        Assert.True(File.Exists(Path.Combine(_root, "PluginAssemblies", "MyPlugin.dll")));
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // ExcludeScriptLibraryWebResources
    // ──────────────────────────────────────────────────────────────────────────────

    private void WriteWebResource(string name)
    {
        var dir = Path.Combine(_root, "WebResources");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), "content");
        File.WriteAllText(Path.Combine(dir, name + ".data.xml"), $"<WebResource><Name>{name}</Name></WebResource>");
    }

    [Fact]
    public void ExcludeWebResource_DeletesContent_KeepsDataXml_WhenMatched()
    {
        WriteWebResource("udpp_main.js");
        var dir = Path.Combine(_root, "WebResources");

        var excluded = SolutionPullTransform.ExcludeScriptLibraryWebResources(_root, new[] { "udpp_main.js" });

        Assert.Equal(new[] { "udpp_main.js" }, excluded);
        Assert.False(File.Exists(Path.Combine(dir, "udpp_main.js")));
        Assert.True(File.Exists(Path.Combine(dir, "udpp_main.js.data.xml")));
    }

    [Fact]
    public void ExcludeWebResource_KeepsContent_WhenNotMatched()
    {
        WriteWebResource("udpp_static.svg");
        var dir = Path.Combine(_root, "WebResources");

        var excluded = SolutionPullTransform.ExcludeScriptLibraryWebResources(_root, new[] { "udpp_main.js" });

        Assert.Empty(excluded);
        Assert.True(File.Exists(Path.Combine(dir, "udpp_static.svg")));
        Assert.True(File.Exists(Path.Combine(dir, "udpp_static.svg.data.xml")));
    }

    [Fact]
    public void ExcludeWebResource_NoOp_WhenNoWebResourcesFolder()
    {
        var excluded = SolutionPullTransform.ExcludeScriptLibraryWebResources(_root, new[] { "udpp_main.js" });
        Assert.Empty(excluded);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // ExcludePcfControls
    // ──────────────────────────────────────────────────────────────────────────────

    private void WriteControlFolder(string stagingRoot, string publisherPrefix, string stagingFolderName)
    {
        // Simulate an unpacked Controls/ entry with a Solution.xml that has the publisher prefix.
        var controlDir = Path.Combine(stagingRoot, "Controls", stagingFolderName);
        Directory.CreateDirectory(controlDir);
        File.WriteAllText(Path.Combine(controlDir, "bundle.js"), "bundle content");

        var otherDir = Path.Combine(stagingRoot, "Other");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(Path.Combine(otherDir, "Solution.xml"),
            $"<ImportExportXml><SolutionManifest><Publisher><CustomizationPrefix>{publisherPrefix}</CustomizationPrefix></Publisher></SolutionManifest></ImportExportXml>");
    }

    [Fact]
    public void ExcludePcf_DeletesMatchingControlFolder()
    {
        WriteControlFolder(_root, "udpp", "udpp_UdppControls_QuantityIndicator");
        // Also add a non-matching control
        Directory.CreateDirectory(Path.Combine(_root, "Controls", "udpp_ThirdParty_Widget"));
        File.WriteAllText(Path.Combine(_root, "Controls", "udpp_ThirdParty_Widget", "bundle.js"), "x");

        var excluded = SolutionPullTransform.ExcludePcfControls(_root,
            new[] { "UdppControls.QuantityIndicator" });

        Assert.Equal(new[] { "udpp_UdppControls_QuantityIndicator" }, excluded);
        Assert.False(Directory.Exists(Path.Combine(_root, "Controls", "udpp_UdppControls_QuantityIndicator")));
        // Non-matching control stays.
        Assert.True(Directory.Exists(Path.Combine(_root, "Controls", "udpp_ThirdParty_Widget")));
    }

    [Fact]
    public void ExcludePcf_NoOp_WhenNoControlsFolder()
    {
        // Write Solution.xml with prefix but no Controls/ dir.
        var otherDir = Path.Combine(_root, "Other");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(Path.Combine(otherDir, "Solution.xml"),
            "<ImportExportXml><SolutionManifest><Publisher><CustomizationPrefix>udpp</CustomizationPrefix></Publisher></SolutionManifest></ImportExportXml>");

        var excluded = SolutionPullTransform.ExcludePcfControls(_root, new[] { "UdppControls.QuantityIndicator" });

        Assert.Empty(excluded);
    }
}
