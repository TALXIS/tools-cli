using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Platform.Dataverse.Application.Pipeline;
using TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionPullPipelineTests : IDisposable
{
    private readonly string _base;
    private readonly IProjectReferenceMetadataReader _projectReferenceReader;

    public SolutionPullPipelineTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "txc_pipeline_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
        _projectReferenceReader = new ProjectReferenceMetadataReader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_base))
            Directory.Delete(_base, recursive: true);
    }

    [Fact]
    public void ReferencedPluginDllExcluded_NonReferencedKept()
    {
        var pluginProj = Path.Combine(_base, "Plugins.Warehouse", "Plugins.Warehouse.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(pluginProj)!);
        File.WriteAllText(pluginProj,
            """
            <Project Sdk="TALXIS.DevKit.Build.Sdk/0.0.0.14">
              <PropertyGroup>
                <ProjectType>Plugin</ProjectType>
                <AssemblyName>PluginsWarehouse</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        var solDir = Path.Combine(_base, "Solutions.Logic");
        Directory.CreateDirectory(solDir);
        var solProj = Path.Combine(solDir, "Solutions.Logic.csproj");
        File.WriteAllText(solProj,
            "<Project Sdk=\"TALXIS.DevKit.Build.Sdk/7.0.0\"><ItemGroup><ProjectReference Include=\"..\\Plugins.Warehouse\\Plugins.Warehouse.csproj\" /></ItemGroup></Project>");

        var staging = Path.Combine(_base, "staging");
        WriteServerAssembly(staging, "PluginsWarehouse-38E8D392-49D6", "PluginsWarehouse", "PluginsWarehouse, Version=1.0.12605.27000, Culture=neutral, PublicKeyToken=73895ec8fc11dc14");
        WriteServerAssembly(staging, "ThirdParty-AAAABBBB-CCCC", "ThirdParty", "ThirdParty, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

        var context = new SolutionPullContext
        {
            StagingDirectory = staging,
            DestinationDirectory = solDir,
            ProjectFilePath = solProj,
            ReferencedProjectDirectories = []
        };

        var steps = new ISolutionPullStep[]
        {
            new SystemRelationshipExclusionStep(),
            new SolutionManifestNormalizationStep(),
            new PluginAssemblyNormalizationStep(NullLogger.Instance),
            new ProjectReferenceBinaryExclusionStep(_projectReferenceReader),
            new ScriptLibraryExclusionStep(_projectReferenceReader, NullLogger.Instance),
            new PcfControlExclusionStep(_projectReferenceReader)
        };

        foreach (var step in steps)
            step.Execute(context);

        SolutionPullMerge.Merge(staging, solDir);

        var pa = Path.Combine(solDir, "PluginAssemblies");

        Assert.Contains("PluginsWarehouse.dll", context.ExcludedBinaries);

        // Referenced plugin: data.xml lands, binary does not.
        Assert.True(File.Exists(Path.Combine(pa, "PluginsWarehouse.dll.data.xml")));
        Assert.False(File.Exists(Path.Combine(pa, "PluginsWarehouse.dll")));

        // Non-referenced plugin: binary stays in the solution root.
        Assert.True(File.Exists(Path.Combine(pa, "ThirdParty.dll.data.xml")));
        Assert.True(File.Exists(Path.Combine(pa, "ThirdParty.dll")));

        // Project file is never touched.
        Assert.True(File.Exists(solProj));
    }

    [Fact]
    public void SharedTable_ExcludedSystemRelationshipsAreNotWritten()
    {
        // skt_product is a root component of this solution and of SkillsTestDataModel.
        var staging = Path.Combine(_base, "staging");
        var other = Path.Combine(staging, "Other");
        Directory.CreateDirectory(Path.Combine(other, "Relationships"));
        File.WriteAllText(Path.Combine(other, "Relationships.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <EntityRelationships xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <EntityRelationship Name="business_unit_skt_product" />
              <EntityRelationship Name="lk_skt_product_createdby" />
              <EntityRelationship Name="lk_skt_product_modifiedby" />
              <EntityRelationship Name="owner_skt_product" />
              <EntityRelationship Name="skt_skt_supplier_skt_product_supplierid" />
              <EntityRelationship Name="team_skt_product" />
              <EntityRelationship Name="TransactionCurrency_skt_product" />
              <EntityRelationship Name="user_skt_product" />
            </EntityRelationships>
            """);
        WriteEntityRelationships(other, "BusinessUnit", "business_unit_skt_product");
        WriteEntityRelationships(other, "Owner", "owner_skt_product");
        WriteEntityRelationships(other, "SystemUser", "lk_skt_product_createdby", "lk_skt_product_modifiedby", "user_skt_product");
        WriteEntityRelationships(other, "Team", "team_skt_product");
        WriteEntityRelationships(other, "TransactionCurrency", "TransactionCurrency_skt_product");
        WriteEntityRelationships(other, "skt_supplier", "skt_skt_supplier_skt_product_supplierid");
        File.WriteAllText(Path.Combine(other, "Solution.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ImportExportXml version="9.2.25063.181" SolutionPackageVersion="9.2" languagecode="1033" generatedBy="CrmLive" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <SolutionManifest>
                <UniqueName>SkillsTestExtra</UniqueName>
                <Version>1.0.0.0</Version>
                <Managed>0</Managed>
                <RootComponents>
                  <RootComponent type="1" schemaName="skt_product" behavior="0" />
                </RootComponents>
                <MissingDependencies>
                  <MissingDependency>
                    <Required type="1" schemaName="systemuser" displayName="User" solution="System (5.0)" />
                    <Dependent type="10" schemaName="lk_skt_product_createdby" displayName="lk_skt_product_createdby" parentSchemaName="skt_product" />
                  </MissingDependency>
                  <MissingDependency>
                    <Required type="1" schemaName="skt_supplier" displayName="Supplier" solution="SkillsTestDataModel" />
                    <Dependent type="10" schemaName="skt_skt_supplier_skt_product_supplierid" displayName="skt_skt_supplier_skt_product_supplierid" parentSchemaName="skt_product" />
                  </MissingDependency>
                </MissingDependencies>
              </SolutionManifest>
            </ImportExportXml>
            """);

        // A previous pull left a system relationship file in the project.
        var solDir = Path.Combine(_base, "SkillsTestExtra");
        Directory.CreateDirectory(Path.Combine(solDir, "Other", "Relationships"));
        WriteEntityRelationships(Path.Combine(solDir, "Other"), "Owner", "owner_skt_product");

        var context = new SolutionPullContext
        {
            StagingDirectory = staging,
            DestinationDirectory = solDir,
            ReferencedProjectDirectories = []
        };

        new SystemRelationshipExclusionStep().Execute(context);
        new SolutionManifestNormalizationStep().Execute(context);
        var removedFiles = SolutionPullMerge.Merge(staging, solDir);

        string[] systemRelationships =
        [
            "business_unit_skt_product", "lk_skt_product_createdby", "lk_skt_product_modifiedby",
            "owner_skt_product", "team_skt_product", "user_skt_product"
        ];
        Assert.Equal(systemRelationships.Order(), context.ExcludedRelationships.Order());

        var written = Directory.GetFiles(solDir, "*.xml", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        foreach (var name in context.ExcludedRelationships)
            Assert.DoesNotContain(written, contents => contents.Contains(name, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            new[] { "skt_supplier.xml", "TransactionCurrency.xml" },
            Directory.GetFiles(Path.Combine(solDir, "Other", "Relationships"))
                .Select(Path.GetFileName)
                .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains(Path.Combine("Other", "Relationships", "Owner.xml"), removedFiles);

        var index = File.ReadAllText(Path.Combine(solDir, "Other", "Relationships.xml"));
        Assert.Contains("TransactionCurrency_skt_product", index);
        Assert.Contains("skt_skt_supplier_skt_product_supplierid", index);

        var solutionXml = File.ReadAllText(Path.Combine(solDir, "Other", "Solution.xml"));
        Assert.Contains("skt_skt_supplier_skt_product_supplierid", solutionXml);
        Assert.Contains("<RootComponent type=\"1\" schemaName=\"skt_product\"", solutionXml);
    }

    private static void WriteEntityRelationships(string otherDirectory, string entity, params string[] names)
    {
        var relationships = string.Concat(names.Select(name =>
            $"""
              <EntityRelationship Name="{name}">
                <EntityRelationshipType>OneToMany</EntityRelationshipType>
                <ReferencingEntityName>skt_product</ReferencingEntityName>
                <ReferencedEntityName>{entity}</ReferencedEntityName>
              </EntityRelationship>

            """));
        File.WriteAllText(
            Path.Combine(otherDirectory, "Relationships", entity + ".xml"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <EntityRelationships xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            {relationships}</EntityRelationships>
            """);
    }

    private static void WriteServerAssembly(string stagingRoot, string nestedFolder, string baseName, string fullName)
    {
        var dir = Path.Combine(stagingRoot, "PluginAssemblies", nestedFolder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, baseName + ".dll"), "binary");
        File.WriteAllText(Path.Combine(dir, baseName + ".dll.data.xml"),
            $"<PluginAssembly FullName=\"{fullName}\"><FileName>/PluginAssemblies/{nestedFolder}/{baseName}.dll</FileName></PluginAssembly>");
    }
}
