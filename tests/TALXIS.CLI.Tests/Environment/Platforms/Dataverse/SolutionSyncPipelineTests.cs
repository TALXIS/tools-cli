using TALXIS.CLI.Core.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class SolutionSyncPipelineTests : IDisposable
{
    private readonly string _base;

    public SolutionSyncPipelineTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "txc_pipeline_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_base);
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

        SolutionSyncTransform.NormalizePluginAssemblyPaths(staging);
        var refs = ProjectReferenceReader.ReadPluginAssemblyNames(solProj);
        var excluded = SolutionSyncTransform.ExcludeProjectReferenceBinaries(staging, refs);
        SolutionSyncMerge.Merge(staging, solDir);

        var pa = Path.Combine(solDir, "PluginAssemblies");

        Assert.Contains("PluginsWarehouse", refs);
        Assert.Contains("PluginsWarehouse.dll", excluded);

        // Referenced plugin: data.xml lands, binary does not.
        Assert.True(File.Exists(Path.Combine(pa, "PluginsWarehouse.dll.data.xml")));
        Assert.False(File.Exists(Path.Combine(pa, "PluginsWarehouse.dll")));

        // Non-referenced plugin: binary stays in the solution root.
        Assert.True(File.Exists(Path.Combine(pa, "ThirdParty.dll.data.xml")));
        Assert.True(File.Exists(Path.Combine(pa, "ThirdParty.dll")));

        // Project file is never touched.
        Assert.True(File.Exists(solProj));
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
