using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Platform.Dataverse.Application.Pipeline;
using TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionPullTransformTests : IDisposable
{
    private readonly ProjectReferenceBinaryExclusionStep _projectReferenceBinaryExclusionStep;
    private readonly PluginAssemblyNormalizationStep _pluginAssemblyNormalizationStep;
    private readonly PcfControlExclusionStep _pcfControlExclusionStep;
    private readonly string _root;
    private readonly ScriptLibraryExclusionStep _scriptLibraryExclusionStep;
    private readonly ExportNormalizationStep _exportNormalizationStep;

    // A non-existent path simulates a first-sync scenario: no local convention established yet.
    // All assemblies are treated as new and default to the flat TALXIS SDK layout.
    private string NoLocalConvention => Path.Combine(Path.GetTempPath(), "no_dest_" + Guid.NewGuid().ToString("N"));

    public SolutionPullTransformTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "txc_sync_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var projectReferenceReader = new ProjectReferenceMetadataReader();
        _projectReferenceBinaryExclusionStep = new ProjectReferenceBinaryExclusionStep(projectReferenceReader);
        _pluginAssemblyNormalizationStep = new PluginAssemblyNormalizationStep(NullLogger.Instance);
        _pcfControlExclusionStep = new PcfControlExclusionStep(projectReferenceReader);
        _scriptLibraryExclusionStep = new ScriptLibraryExclusionStep(projectReferenceReader, NullLogger.Instance);
        _exportNormalizationStep = new ExportNormalizationStep();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private SolutionPullContext CreateContext(
        string destinationRoot,
        IReadOnlyCollection<string>? referencedPluginAssemblyNames = null,
        IReadOnlyCollection<string>? referencedScriptLibraryWebResources = null,
        IReadOnlyCollection<string>? referencedPcfControlNames = null,
        string? projectFilePath = null)
        => new()
        {
            StagingDirectory = _root,
            DestinationDirectory = destinationRoot,
            ProjectFilePath = projectFilePath,
            ReferencedProjectDirectories = [],
            ReferencedPluginAssemblyNames = referencedPluginAssemblyNames,
            ReferencedScriptLibraryWebResources = referencedScriptLibraryWebResources,
            ReferencedPcfControlNames = referencedPcfControlNames
        };

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

        var context = CreateContext(NoLocalConvention);
        _pluginAssemblyNormalizationStep.Execute(context);

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Equal(new[] { "Acme.MyPlugin" }, context.NormalizedAssemblies);
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

        var context = CreateContext(NoLocalConvention);
        _pluginAssemblyNormalizationStep.Execute(context);

        Assert.Empty(context.NormalizedAssemblies);
        Assert.True(File.Exists(Path.Combine(pluginsDir, "Flat.dll")));
    }

    [Fact]
    public void Restore_NoOp_WhenNoPluginAssembliesFolder()
    {
        var context = CreateContext(NoLocalConvention);
        _pluginAssemblyNormalizationStep.Execute(context);
        Assert.Empty(context.NormalizedAssemblies);
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
            var context = CreateContext(destRoot);
            _pluginAssemblyNormalizationStep.Execute(context);

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
            var context = CreateContext(destRoot);
            _pluginAssemblyNormalizationStep.Execute(context);

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

        var context = CreateContext(NoLocalConvention, referencedPluginAssemblyNames: new[] { "MyPlugin" });
        _pluginAssemblyNormalizationStep.Execute(context);
        _projectReferenceBinaryExclusionStep.Execute(context);

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Equal(new[] { "MyPlugin.dll" }, context.ExcludedBinaries);
        Assert.False(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsDir, "MyPlugin.dll.data.xml")));
    }

    [Fact]
    public void Exclude_MatchesDottedNamespaceExtension()
    {
        WriteAssembly("X", "MoveOrder.Logic", "Acme.Apps.MoveOrder.Logic, Version=1.0.0.0", "/PluginAssemblies/MoveOrder.Logic.dll");

        var context = CreateContext(NoLocalConvention, referencedPluginAssemblyNames: new[] { "MoveOrder.Logic" });
        _pluginAssemblyNormalizationStep.Execute(context);
        _projectReferenceBinaryExclusionStep.Execute(context);

        Assert.Equal(new[] { "MoveOrder.Logic.dll" }, context.ExcludedBinaries);
    }

    [Fact]
    public void Exclude_DoesNotFalsePositive_OnUnrelatedSuffixMatch()
    {
        // Third-party assembly "Logic" must NOT be deleted just because a referenced project
        // has AssemblyName "Acme.Apps.MoveOrder.Logic" (which ends with ".Logic").
        WriteAssembly("A", "Logic", "Logic, Version=1.0.0.0", "/PluginAssemblies/Logic.dll");
        WriteAssembly("B", "MoveOrder.Logic", "Acme.Apps.MoveOrder.Logic, Version=1.0.0.0", "/PluginAssemblies/MoveOrder.Logic.dll");

        var context = CreateContext(NoLocalConvention, referencedPluginAssemblyNames: new[] { "MoveOrder.Logic" });
        _pluginAssemblyNormalizationStep.Execute(context);
        _projectReferenceBinaryExclusionStep.Execute(context);

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Equal(new[] { "MoveOrder.Logic.dll" }, context.ExcludedBinaries);
        // Third-party "Logic.dll" must survive.
        Assert.True(File.Exists(Path.Combine(pluginsDir, "Logic.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsDir, "Logic.dll.data.xml")));
    }

    [Fact]
    public void Exclude_KeepsBinary_WhenNotReferenced()
    {
        WriteAssembly("X", "ThirdParty", "ThirdParty, Version=1.0.0.0", "/PluginAssemblies/ThirdParty.dll");

        var context = CreateContext(NoLocalConvention, referencedPluginAssemblyNames: new[] { "MyPlugin" });
        _pluginAssemblyNormalizationStep.Execute(context);
        _projectReferenceBinaryExclusionStep.Execute(context);

        var pluginsDir = Path.Combine(_root, "PluginAssemblies");
        Assert.Empty(context.ExcludedBinaries);
        Assert.True(File.Exists(Path.Combine(pluginsDir, "ThirdParty.dll")));
    }

    [Fact]
    public void Exclude_NoOp_WhenNoReferences()
    {
        WriteAssembly("X", "MyPlugin", "MyPlugin, Version=1.0.0.0", "/PluginAssemblies/MyPlugin.dll");

        var context = CreateContext(NoLocalConvention, referencedPluginAssemblyNames: Array.Empty<string>());
        _pluginAssemblyNormalizationStep.Execute(context);
        _projectReferenceBinaryExclusionStep.Execute(context);

        Assert.Empty(context.ExcludedBinaries);
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

    private void WriteSolutionManifest(string root, string version, string managed, string rootAttributes = "", string rootComponents = "<RootComponent type=\"1\" schemaName=\"tprt_testitem\" behavior=\"0\" />")
    {
        var otherDir = Path.Combine(root, "Other");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(
            Path.Combine(otherDir, "Solution.xml"),
            $"""
             <ImportExportXml{rootAttributes}>
               <SolutionManifest>
                 <UniqueName>TestSolution</UniqueName>
                 <Version>{version}</Version>
                 <Managed>{managed}</Managed>
                 <RootComponents>{rootComponents}</RootComponents>
               </SolutionManifest>
             </ImportExportXml>
             """);
    }

    private void WriteRelationshipsFile(string contents)
    {
        var otherDir = Path.Combine(_root, "Other");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(Path.Combine(otherDir, "Relationships.xml"), contents);
    }

    [Fact]
    public void ExcludeWebResource_DeletesContent_KeepsDataXml_WhenMatched()
    {
        WriteWebResource("udpp_main.js");
        var dir = Path.Combine(_root, "WebResources");

        var context = CreateContext(NoLocalConvention, referencedScriptLibraryWebResources: new[] { "udpp_main.js" });
        _scriptLibraryExclusionStep.Execute(context);

        Assert.Equal(new[] { "udpp_main.js" }, context.ExcludedWebResources);
        Assert.False(File.Exists(Path.Combine(dir, "udpp_main.js")));
        Assert.True(File.Exists(Path.Combine(dir, "udpp_main.js.data.xml")));
    }

    [Fact]
    public void ExcludeWebResource_KeepsContent_WhenNotMatched()
    {
        WriteWebResource("udpp_static.svg");
        var dir = Path.Combine(_root, "WebResources");

        var context = CreateContext(NoLocalConvention, referencedScriptLibraryWebResources: new[] { "udpp_main.js" });
        _scriptLibraryExclusionStep.Execute(context);

        Assert.Empty(context.ExcludedWebResources);
        Assert.True(File.Exists(Path.Combine(dir, "udpp_static.svg")));
        Assert.True(File.Exists(Path.Combine(dir, "udpp_static.svg.data.xml")));
    }

    [Fact]
    public void ExcludeWebResource_NoOp_WhenNoWebResourcesFolder()
    {
        var context = CreateContext(NoLocalConvention, referencedScriptLibraryWebResources: new[] { "udpp_main.js" });
        _scriptLibraryExclusionStep.Execute(context);
        Assert.Empty(context.ExcludedWebResources);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // ExportNormalizationStep — manifest normalization
    // ──────────────────────────────────────────────────────────────────────────────

    private string CreateDestination(string version = "1.0.0.0", string managed = "2")
    {
        var destinationRoot = Path.Combine(Path.GetTempPath(), "dest_" + Guid.NewGuid().ToString("N"));
        WriteSolutionManifest(destinationRoot, version, managed);
        return destinationRoot;
    }

    [Fact]
    public void Normalize_PreservesLocalManifestAsSourceOfTruth()
    {
        WriteSolutionManifest(_root, "1.0.12606.28000", "0");
        var destinationRoot = CreateDestination(version: "1.0.0.42", managed: "2");

        try
        {
            var context = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(context);

            var stagingManifest = File.ReadAllText(Path.Combine(_root, "Other", "Solution.xml"));
            var localManifest = File.ReadAllText(Path.Combine(destinationRoot, "Other", "Solution.xml"));
            Assert.Equal(localManifest, stagingManifest);
            Assert.Empty(context.NormalizationChanges);
        }
        finally
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
    }

    [Fact]
    public void Normalize_DeclaresPulledSubcomponentsOfIncludedEntity()
    {
        const string formId = "9c7e6ba6-1111-2222-3333-444444444444";
        const string viewId = "9c7e6ba6-5555-6666-7777-888888888888";
        WriteSolutionManifest(_root, "1.0.0.0", "2");
        var formDir = Path.Combine(_root, "Entities", "tprt_testitem", "FormXml", "main");
        Directory.CreateDirectory(formDir);
        File.WriteAllText(Path.Combine(formDir, $"{{{formId}}}.xml"),
            $$"""
            <forms>
              <systemform>
                <formid>{{{formId}}}</formid>
                <LocalizedNames><LocalizedName description="Main form" languagecode="1033" /></LocalizedNames>
              </systemform>
            </forms>
            """);
        var viewDir = Path.Combine(_root, "Entities", "tprt_testitem", "SavedQueries");
        Directory.CreateDirectory(viewDir);
        File.WriteAllText(Path.Combine(viewDir, $"{{{viewId}}}.xml"),
            $$"""
            <savedqueries>
              <savedquery>
                <savedqueryid>{{{viewId}}}</savedqueryid>
                <querytype>0</querytype>
                <LocalizedNames><LocalizedName description="Active items" languagecode="1033" /></LocalizedNames>
              </savedquery>
            </savedqueries>
            """);
        var destinationRoot = CreateDestination();

        try
        {
            var context = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(context);

            var document = XDocument.Load(Path.Combine(_root, "Other", "Solution.xml"));
            var formComponent = document.Descendants("RootComponent")
                .SingleOrDefault(c => c.Attribute("type")?.Value == "60");
            Assert.NotNull(formComponent);
            Assert.Contains(formId, formComponent!.Attribute("id")?.Value, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(context.NormalizationChanges, c => c.Contains("Declared form"));

            Assert.Empty(document.Descendants("RootComponent").Where(c => c.Attribute("type")?.Value == "26"));
            Assert.DoesNotContain(context.NormalizationChanges, c => c.Contains("Declared view"));

            File.Copy(Path.Combine(_root, "Other", "Solution.xml"), Path.Combine(destinationRoot, "Other", "Solution.xml"), overwrite: true);
            var secondContext = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(secondContext);

            var secondDocument = XDocument.Load(Path.Combine(_root, "Other", "Solution.xml"));
            Assert.Single(secondDocument.Descendants("RootComponent").Where(c => c.Attribute("type")?.Value == "60"));
            Assert.DoesNotContain(secondContext.NormalizationChanges, c => c.Contains("Declared form"));
        }
        finally
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
    }

    [Fact]
    public void Normalize_RunsOnFreshScaffoldWithMismatchedUniqueName()
    {
        const string bootstrapFormId = "1a2b3c4d-0001-4002-8003-000000000042";
        WriteSolutionManifest(_root, "1.0.0.0", "0");
        WriteRelationshipsFile(
            """
            <EntityRelationships xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <EntityRelationship Name="owner_tprt_testitem" />
              <EntityRelationship Name="tprt_testitem_tprt_custom" />
            </EntityRelationships>
            """);
        var bootstrapFormDir = Path.Combine(_root, "Entities", "tprt_testitem", "FormXml", "main");
        Directory.CreateDirectory(bootstrapFormDir);
        File.WriteAllText(Path.Combine(bootstrapFormDir, $"{{{bootstrapFormId}}}.xml"),
            $$"""
            <forms>
              <systemform>
                <formid>{{{bootstrapFormId}}}</formid>
                <LocalizedNames><LocalizedName description="Main form" languagecode="1033" /></LocalizedNames>
              </systemform>
            </forms>
            """);
        var destinationRoot = Path.Combine(Path.GetTempPath(), "dest_" + Guid.NewGuid().ToString("N"));
        var destinationOther = Path.Combine(destinationRoot, "Other");
        Directory.CreateDirectory(destinationOther);
        File.WriteAllText(Path.Combine(destinationOther, "Solution.xml"),
            """
            <ImportExportXml>
              <SolutionManifest>
                <UniqueName>pulled</UniqueName>
                <Version>1.0</Version>
                <Managed>2</Managed>
                <RootComponents />
              </SolutionManifest>
            </ImportExportXml>
            """);

        try
        {
            var context = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(context);

            var document = XDocument.Load(Path.Combine(_root, "Other", "Relationships.xml"));
            var remaining = document.Descendants("EntityRelationship")
                .Select(element => element.Attribute("Name")?.Value)
                .ToArray();
            Assert.Equal(new[] { "tprt_testitem_tprt_custom" }, remaining);
            Assert.Contains("owner_tprt_testitem", context.ExcludedRelationships);

            var manifest = XDocument.Load(Path.Combine(_root, "Other", "Solution.xml"));
            var formComponent = manifest.Descendants("RootComponent")
                .SingleOrDefault(c => c.Attribute("type")?.Value == "60");
            Assert.NotNull(formComponent);
            Assert.Contains(bootstrapFormId, formComponent!.Attribute("id")?.Value, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
    }

    [Fact]
    public void Normalize_KeepsLocalOnlyRootComponentAfterPull()
    {
        WriteSolutionManifest(_root, "1.0.0.0", "2");
        var destinationRoot = Path.Combine(Path.GetTempPath(), "dest_" + Guid.NewGuid().ToString("N"));
        WriteSolutionManifest(destinationRoot, "1.0.0.0", "2",
            rootComponents: "<RootComponent type=\"1\" schemaName=\"tprt_testitem\" behavior=\"0\" /><RootComponent type=\"60\" id=\"{6db7bd1a-9a3e-477c-85c5-c819e39b5272}\" behavior=\"0\" />");

        try
        {
            var context = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(context);

            var document = XDocument.Load(Path.Combine(_root, "Other", "Solution.xml"));
            var components = document.Descendants("RootComponent").ToArray();
            Assert.Equal(2, components.Length);
            Assert.Contains(components, c => c.Attribute("type")?.Value == "60");
        }
        finally
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
    }

    [Fact]
    public void Normalize_Skips_WhenNoLocalSolutionXml()
    {
        WriteSolutionManifest(_root, "1.0.12606.28000", "1");

        var context = CreateContext(NoLocalConvention);
        _exportNormalizationStep.Execute(context);

        var document = XDocument.Load(Path.Combine(_root, "Other", "Solution.xml"));
        var manifest = document.Descendants("SolutionManifest").Single();
        Assert.Equal("1.0.12606.28000", manifest.Element("Version")?.Value);
        Assert.Equal("1", manifest.Element("Managed")?.Value);
        Assert.Empty(context.NormalizationChanges);
    }

    [Fact]
    public void Normalize_StripsServerVersionAttributes()
    {
        WriteSolutionManifest(_root, "1.0.0.0", "2",
            rootAttributes: " OrganizationVersion=\"9.2.25092.135\" OrganizationSchemaType=\"Standard\" CRMServerServiceabilityVersion=\"9.2.25092.00139\"");
        var destinationRoot = CreateDestination();

        try
        {
            var context = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(context);

            var root = XDocument.Load(Path.Combine(_root, "Other", "Solution.xml")).Root!;
            Assert.Null(root.Attribute("OrganizationVersion"));
            Assert.Null(root.Attribute("OrganizationSchemaType"));
            Assert.Null(root.Attribute("CRMServerServiceabilityVersion"));
            Assert.NotEmpty(context.NormalizationChanges);
        }
        finally
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // ExportNormalizationStep — system relationship exclusion
    // ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_RemovesKnownSystemRelationshipPatterns()
    {
        WriteSolutionManifest(_root, "1.0.0.0", "2");
        WriteRelationshipsFile(
            """
            <EntityRelationships xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <EntityRelationship Name="business_unit_tprt_testitem" />
              <EntityRelationship Name="lk_tprt_testitem_createdby" />
              <EntityRelationship Name="lk_tprt_testitem_modifiedby" />
              <EntityRelationship Name="owner_tprt_testitem" />
              <EntityRelationship Name="team_tprt_testitem" />
              <EntityRelationship Name="user_tprt_testitem" />
              <EntityRelationship Name="tprt_testitem_tprt_custom" />
            </EntityRelationships>
            """);
        var destinationRoot = CreateDestination();

        try
        {
            var context = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(context);

            var document = XDocument.Load(Path.Combine(_root, "Other", "Relationships.xml"));
            var remaining = document.Descendants("EntityRelationship")
                .Select(element => element.Attribute("Name")?.Value)
                .Where(name => name is not null)
                .ToArray();
            Assert.Equal(new[] { "tprt_testitem_tprt_custom" }, remaining);
            Assert.Equal(6, context.ExcludedRelationships.Count);
        }
        finally
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
    }

    [Fact]
    public void Normalize_KeepsCustomAndLocallyPresentRelationships()
    {
        WriteSolutionManifest(_root, "1.0.0.0", "2");
        WriteRelationshipsFile(
            """
            <EntityRelationships xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <EntityRelationship Name="tprt_testitem_tprt_custom" />
              <EntityRelationship Name="owner_tprt_testitem" />
            </EntityRelationships>
            """);
        var destinationRoot = CreateDestination();
        var destinationOther = Path.Combine(destinationRoot, "Other");
        File.WriteAllText(Path.Combine(destinationOther, "Relationships.xml"),
            """
            <EntityRelationships xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <EntityRelationship Name="owner_tprt_testitem" />
            </EntityRelationships>
            """);

        try
        {
            var context = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(context);

            var document = XDocument.Load(Path.Combine(_root, "Other", "Relationships.xml"));
            var remaining = document.Descendants("EntityRelationship")
                .Select(element => element.Attribute("Name")?.Value)
                .Where(name => name is not null)
                .ToArray();
            Assert.Empty(context.ExcludedRelationships);
            Assert.Equal(new[] { "tprt_testitem_tprt_custom", "owner_tprt_testitem" }, remaining);
        }
        finally
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
    }

    [Fact]
    public void Normalize_RemovesEntityNotInLocalSolution()
    {
        WriteSolutionManifest(_root, "1.0.0.0", "2");
        var entityDir = Path.Combine(_root, "Entities", "tprt_leaked");
        Directory.CreateDirectory(entityDir);
        File.WriteAllText(Path.Combine(entityDir, "Entity.xml"),
            """
            <Entity xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <EntityInfo>
                <entity Name="tprt_leaked">
                  <LocalizedNames><LocalizedName description="Leaked" languagecode="1033" /></LocalizedNames>
                  <attributes />
                </entity>
              </EntityInfo>
            </Entity>
            """);
        var destinationRoot = CreateDestination();

        try
        {
            var context = CreateContext(destinationRoot);
            _exportNormalizationStep.Execute(context);

            Assert.False(Directory.Exists(entityDir));
            Assert.Contains(context.NormalizationChanges, c => c.Contains("tprt_leaked"));
        }
        finally
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
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

        var context = CreateContext(NoLocalConvention, referencedPcfControlNames: new[] { "UdppControls.QuantityIndicator" });
        _pcfControlExclusionStep.Execute(context);

        Assert.Equal(new[] { "udpp_UdppControls_QuantityIndicator" }, context.ExcludedPcfControls);
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

        var context = CreateContext(NoLocalConvention, referencedPcfControlNames: new[] { "UdppControls.QuantityIndicator" });
        _pcfControlExclusionStep.Execute(context);

        Assert.Empty(context.ExcludedPcfControls);
    }
}
