using System.Xml.Linq;

namespace TALXIS.CLI.Core.Resolution;

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

    public static IReadOnlyCollection<string> ReadScriptLibraryWebResourceNames(string solutionProjectFilePath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var publisherPrefix = ReadProperty(solutionProjectFilePath, "PublisherPrefix");
        if (string.IsNullOrWhiteSpace(publisherPrefix))
            return result;

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
        var doc = XDocument.Load(projectFilePath);
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
