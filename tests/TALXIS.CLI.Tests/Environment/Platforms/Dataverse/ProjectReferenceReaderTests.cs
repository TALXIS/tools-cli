using TALXIS.CLI.Core.Resolution;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class ProjectReferenceReaderTests : IDisposable
{
    private readonly string _root;

    public ProjectReferenceReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "txc_projref_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteReferencedProject(string folder, string fileName, string innerXml)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, $"<Project Sdk=\"TALXIS.DevKit.Build.Sdk/1.0.0\"><PropertyGroup>{innerXml}</PropertyGroup></Project>");
        return path;
    }

    private string WriteSolution(params string[] includes)
    {
        var refs = string.Concat(includes.Select(i => $"<ProjectReference Include=\"{i}\" />"));
        var path = Path.Combine(_root, "Solution.cdsproj");
        File.WriteAllText(path, $"<Project Sdk=\"TALXIS.DevKit.Build.Sdk/1.0.0\"><ItemGroup>{refs}</ItemGroup></Project>");
        return path;
    }

    [Fact]
    public void Reads_AssemblyName_ForPluginProject()
    {
        WriteReferencedProject("Logic", "Logic.csproj", "<ProjectType>Plugin</ProjectType><AssemblyName>Acme.Apps.MoveOrder.Logic</AssemblyName>");
        var cdsproj = WriteSolution("Logic\\Logic.csproj");

        var names = ProjectReferenceReader.ReadPluginAssemblyNames(cdsproj);

        Assert.Contains("Acme.Apps.MoveOrder.Logic", names);
    }

    [Fact]
    public void Includes_WorkflowActivityProject()
    {
        WriteReferencedProject("Wf", "Wf.csproj", "<ProjectType>WorkflowActivity</ProjectType><AssemblyName>Acme.Wf</AssemblyName>");
        var cdsproj = WriteSolution("Wf\\Wf.csproj");

        var names = ProjectReferenceReader.ReadPluginAssemblyNames(cdsproj);

        Assert.Contains("Acme.Wf", names);
    }

    [Fact]
    public void Ignores_NonPluginProjectTypes()
    {
        WriteReferencedProject("Lib", "Lib.csproj", "<AssemblyName>Shared.Lib</AssemblyName>");
        WriteReferencedProject("Script", "Script.csproj", "<ProjectType>ScriptLibrary</ProjectType><AssemblyName>Scripts</AssemblyName>");
        var cdsproj = WriteSolution("Lib\\Lib.csproj", "Script\\Script.csproj");

        var names = ProjectReferenceReader.ReadPluginAssemblyNames(cdsproj);

        Assert.Empty(names);
    }

    [Fact]
    public void FallsBackTo_ProjectFileName_WhenNoAssemblyName()
    {
        WriteReferencedProject("Logic", "MyPlugin.csproj", "<ProjectType>Plugin</ProjectType>");
        var cdsproj = WriteSolution("Logic\\MyPlugin.csproj");

        var names = ProjectReferenceReader.ReadPluginAssemblyNames(cdsproj);

        Assert.Contains("MyPlugin", names);
    }

    [Fact]
    public void Returns_Empty_WhenNoProjectReferences()
    {
        var cdsproj = Path.Combine(_root, "Solution.cdsproj");
        File.WriteAllText(cdsproj, "<Project Sdk=\"TALXIS.DevKit.Build.Sdk/1.0.0\"><PropertyGroup /></Project>");

        var names = ProjectReferenceReader.ReadPluginAssemblyNames(cdsproj);

        Assert.Empty(names);
    }

    [Fact]
    public void Returns_Empty_WhenProjectMissing()
    {
        var names = ProjectReferenceReader.ReadPluginAssemblyNames(Path.Combine(_root, "nope.cdsproj"));
        Assert.Empty(names);
    }
}
