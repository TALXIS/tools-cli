using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Core.Resolution;

/// <summary>
/// Reads plugin assembly names, script-library web resource names, and PCF control names
/// from TALXIS SDK–style solution project files.
/// </summary>
/// <remarks>
/// Component detection relies on the <c>&lt;ProjectType&gt;</c> MSBuild property, which is a
/// TALXIS SDK convention. Repos initialized with <c>pac solution init</c> / <c>pac plugin init</c>
/// do <b>not</b> include <c>&lt;ProjectType&gt;</c>.
///
/// TODO: Support pac-init repos. pac detects components by file extension:
/// <list type="bullet">
///   <item><c>.csproj</c> = plugin; read <c>&lt;PackageId&gt;</c> if <c>Sdk="Microsoft.NET.Sdk"</c>,
///         otherwise <c>&lt;AssemblyName&gt;</c>; fallback = file name without extension.
///         pac also recurses transitive <c>&lt;ProjectReference&gt;</c> chains.</item>
///   <item><c>.pcfproj</c> = PCF control; find <c>ControlManifest.Input.xml</c> under the project
///         directory and return <c>{namespace}.{constructor}</c>.</item>
/// </list>
/// </remarks>
public static class ProjectReferenceReader
{
    private static readonly HashSet<string> PluginProjectTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Plugin", "WorkflowActivity" };

    public static IReadOnlyCollection<string> ReadPluginAssemblyNames(string projectFilePath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var referenced in EnumerateReferencedProjects(projectFilePath))
        {
            var info = ReadProjectInfo(referenced);
            if (info.ProjectType is not null && PluginProjectTypes.Contains(info.ProjectType))
                result.Add(info.AssemblyName);
        }
        return result;
    }

    public static IReadOnlyCollection<string> ReadScriptLibraryWebResourceNames(
        string solutionProjectFilePath,
        ILogger? logger = null)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var publisherPrefix = ReadProperty(solutionProjectFilePath, "PublisherPrefix");
        if (string.IsNullOrWhiteSpace(publisherPrefix))
        {
            logger?.LogWarning(
                "<PublisherPrefix> is not set in '{ProjectFile}'. " +
                "ScriptLibrary web resource binaries will not be excluded from the sync — " +
                "they may appear as changed files in git. Add <PublisherPrefix> to the project to fix this.",
                solutionProjectFilePath);
            return result;
        }

        foreach (var referenced in EnumerateReferencedProjects(solutionProjectFilePath))
        {
            var info = ReadProjectInfo(referenced);
            if (string.Equals(info.ProjectType, "ScriptLibrary", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(info.ScriptLibraryName))
                result.Add(info.ScriptLibraryName.StartsWith(publisherPrefix) ? $"{info.ScriptLibraryName}.js" : $"{publisherPrefix}_{info.ScriptLibraryName}.js");
        }
        return result;
    }

    private static IEnumerable<string> EnumerateReferencedProjects(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
            yield break;

        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;
        XDocument doc;
        try
        {
            doc = XDocument.Load(projectFilePath);
        }
        catch (System.Xml.XmlException)
        {
            yield break;
        }
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        foreach (var reference in doc.Descendants(ns + "ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
                continue;

            var relative = include.Replace('\\', Path.DirectorySeparatorChar);
            var referencedPath = Path.GetFullPath(Path.Combine(projectDir, relative));
            if (File.Exists(referencedPath))
                yield return referencedPath;
        }
    }

    private static (string? ProjectType, string AssemblyName, string? ScriptLibraryName) ReadProjectInfo(string referencedProjectPath)
    {
        var fallbackName = Path.GetFileNameWithoutExtension(referencedProjectPath);
        try
        {
            var doc = XDocument.Load(referencedProjectPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var projectType = doc.Descendants(ns + "ProjectType").FirstOrDefault()?.Value?.Trim();
            var assemblyName = doc.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value?.Trim();
            var scriptLibraryName = doc.Descendants(ns + "ScriptLibraryName").FirstOrDefault()?.Value?.Trim();
            return (projectType, string.IsNullOrWhiteSpace(assemblyName) ? fallbackName : assemblyName, scriptLibraryName);
        }
        catch (System.Xml.XmlException)
        {
            return (null, fallbackName, null);
        }
    }

    /// <summary>
    /// Reads PCF control fully-qualified names (<c>{namespace}.{constructor}</c>) from
    /// <c>.pcfproj</c> references in <paramref name="solutionProjectFilePath"/>.
    /// </summary>
    /// <remarks>
    /// For each referenced <c>.pcfproj</c>, searches recursively under the project directory
    /// for <c>ControlManifest.Input.xml</c> and returns the value of
    /// <c>namespace + "." + constructor</c> attributes on the <c>&lt;control&gt;</c> element.
    /// </remarks>
    public static IReadOnlyCollection<string> ReadPcfControlNames(string solutionProjectFilePath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var referenced in EnumerateReferencedProjects(solutionProjectFilePath))
        {
            if (!string.Equals(Path.GetExtension(referenced), ".pcfproj", StringComparison.OrdinalIgnoreCase))
                continue;

            var manifestPath = FindControlManifest(Path.GetDirectoryName(referenced)!);
            if (manifestPath is null)
                continue;

            var controlName = ReadPcfControlName(manifestPath);
            if (!string.IsNullOrWhiteSpace(controlName))
                result.Add(controlName);
        }
        return result;
    }

    private static string? FindControlManifest(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory))
            return null;
        return Directory.EnumerateFiles(projectDirectory, "ControlManifest.Input.xml", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static string? ReadPcfControlName(string manifestPath)
    {
        try
        {
            var doc = XDocument.Load(manifestPath);
            // <control namespace="Acme" constructor="MyControl" ...>
            var control = doc.Descendants("control").FirstOrDefault();
            var ns = control?.Attribute("namespace")?.Value?.Trim();
            var name = control?.Attribute("constructor")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(name))
                return null;
            return $"{ns}.{name}";
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string? ReadProperty(string projectFilePath, string propertyName)
    {
        if (!File.Exists(projectFilePath))
            return null;
        try
        {
            var doc = XDocument.Load(projectFilePath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            return doc.Descendants(ns + propertyName).FirstOrDefault()?.Value?.Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
