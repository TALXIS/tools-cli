using System.Xml.Linq;

namespace TALXIS.CLI.Core.Resolution;

public static class ProjectReferenceReader
{
    private static readonly HashSet<string> PluginProjectTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Plugin", "WorkflowActivity" };

    public static IReadOnlyCollection<string> ReadPluginAssemblyNames(string projectFilePath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(projectFilePath))
            return result;

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
            if (!File.Exists(referencedPath))
                continue;

            var (projectType, assemblyName) = ReadProjectInfo(referencedPath);
            if (projectType is not null && PluginProjectTypes.Contains(projectType))
                result.Add(assemblyName);
        }

        return result;
    }

    private static (string? ProjectType, string AssemblyName) ReadProjectInfo(string referencedProjectPath)
    {
        var fallbackName = Path.GetFileNameWithoutExtension(referencedProjectPath);
        try
        {
            var doc = XDocument.Load(referencedProjectPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var projectType = doc.Descendants(ns + "ProjectType").FirstOrDefault()?.Value?.Trim();
            var assemblyName = doc.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value?.Trim();
            return (projectType, string.IsNullOrWhiteSpace(assemblyName) ? fallbackName : assemblyName);
        }
        catch (System.Xml.XmlException)
        {
            return (null, fallbackName);
        }
    }
}
