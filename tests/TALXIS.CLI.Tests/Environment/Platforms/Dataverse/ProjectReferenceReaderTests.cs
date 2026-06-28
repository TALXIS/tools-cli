using Microsoft.Extensions.Logging;
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

    private string WriteSolutionWithPrefix(string publisherPrefix, params string[] includes)
    {
        var refs = string.Concat(includes.Select(i => $"<ProjectReference Include=\"{i}\" />"));
        var path = Path.Combine(_root, "Solution.cdsproj");
        File.WriteAllText(path, $"<Project Sdk=\"TALXIS.DevKit.Build.Sdk/1.0.0\"><PropertyGroup><PublisherPrefix>{publisherPrefix}</PublisherPrefix></PropertyGroup><ItemGroup>{refs}</ItemGroup></Project>");
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

    [Fact]
    public void ScriptLibrary_BuildsWebResourceName_FromSolutionPrefixAndScriptLibraryName()
    {
        WriteReferencedProject("Scripts", "Scripts.csproj", "<ProjectType>ScriptLibrary</ProjectType><ScriptLibraryName>main</ScriptLibraryName>");
        var cdsproj = WriteSolutionWithPrefix("udpp", "Scripts\\Scripts.csproj");

        var names = ProjectReferenceReader.ReadScriptLibraryWebResourceNames(cdsproj);

        Assert.Contains("udpp_main.js", names);
    }

    [Fact]
    public void ScriptLibrary_Ignores_NonScriptLibraryProjects()
    {
        WriteReferencedProject("Logic", "Logic.csproj", "<ProjectType>Plugin</ProjectType><AssemblyName>Logic</AssemblyName>");
        var cdsproj = WriteSolutionWithPrefix("udpp", "Logic\\Logic.csproj");

        var names = ProjectReferenceReader.ReadScriptLibraryWebResourceNames(cdsproj);

        Assert.Empty(names);
    }

    [Fact]
    public void ScriptLibrary_Empty_WhenSolutionHasNoPublisherPrefix()
    {
        WriteReferencedProject("Scripts", "Scripts.csproj", "<ProjectType>ScriptLibrary</ProjectType><ScriptLibraryName>main</ScriptLibraryName>");
        var cdsproj = WriteSolution("Scripts\\Scripts.csproj");

        var names = ProjectReferenceReader.ReadScriptLibraryWebResourceNames(cdsproj);

        Assert.Empty(names);
    }

    [Fact]
    public void ScriptLibrary_LogsWarning_WhenPublisherPrefixMissing()
    {
        WriteReferencedProject("Scripts", "Scripts.csproj", "<ProjectType>ScriptLibrary</ProjectType><ScriptLibraryName>main</ScriptLibraryName>");
        var cdsproj = WriteSolution("Scripts\\Scripts.csproj");

        var captured = new List<(LogLevel Level, string Message)>();
        var logger = new CaptureLogger(captured);

        var names = ProjectReferenceReader.ReadScriptLibraryWebResourceNames(cdsproj, logger);

        Assert.Empty(names);
        Assert.Contains(captured, e => e.Level == LogLevel.Warning && e.Message.Contains("PublisherPrefix"));
    }

    [Fact]
    public void PcfControl_ReturnsNamespaceDotConstructor_FromPcfproj()
    {
        // Write a .pcfproj with a ControlManifest.Input.xml alongside it.
        var pcfDir = Path.Combine(_root, "Controls.QuantityIndicator");
        Directory.CreateDirectory(pcfDir);
        var pcfProj = Path.Combine(pcfDir, "Controls.QuantityIndicator.pcfproj");
        File.WriteAllText(pcfProj, "<Project Sdk=\"Microsoft.PowerApps.MSBuild.Pcf/1.0.0\"></Project>");
        var manifestDir = Path.Combine(pcfDir, "QuantityIndicator");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "ControlManifest.Input.xml"),
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <manifest>
              <control namespace="UdppControls" constructor="QuantityIndicator" version="0.0.1"
                       display-name-key="Qty Indicator" description-key="" control-type="standard">
              </control>
            </manifest>
            """);

        var cdsproj = WriteSolution($"Controls.QuantityIndicator\\Controls.QuantityIndicator.pcfproj");

        var names = ProjectReferenceReader.ReadPcfControlNames(cdsproj);

        Assert.Single(names);
        Assert.Contains("UdppControls.QuantityIndicator", names);
    }

    [Fact]
    public void PcfControl_Empty_WhenNoManifestFound()
    {
        var pcfDir = Path.Combine(_root, "Controls.Missing");
        Directory.CreateDirectory(pcfDir);
        var pcfProj = Path.Combine(pcfDir, "Controls.Missing.pcfproj");
        File.WriteAllText(pcfProj, "<Project />");

        var cdsproj = WriteSolution($"Controls.Missing\\Controls.Missing.pcfproj");

        var names = ProjectReferenceReader.ReadPcfControlNames(cdsproj);

        Assert.Empty(names);
    }
}

/// <summary>Minimal <see cref="ILogger"/> that records log entries for assertions.</summary>
file sealed class CaptureLogger(List<(LogLevel Level, string Message)> sink) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => sink.Add((logLevel, formatter(state, exception)));
}
