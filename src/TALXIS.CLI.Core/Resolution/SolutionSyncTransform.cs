using System.Xml.Linq;

namespace TALXIS.CLI.Core.Resolution;

public static class SolutionSyncTransform
{
    private const string PluginAssembliesDir = "PluginAssemblies";
    private const string WebResourcesDir = "WebResources";
    private const string DataXmlSuffix = ".data.xml";

    public static IReadOnlyList<string> NormalizePluginAssemblyPaths(string unpackedRoot)
    {
        var normalized = new List<string>();
        var pluginsDir = Path.Combine(unpackedRoot, PluginAssembliesDir);
        if (!Directory.Exists(pluginsDir))
            return normalized;

        foreach (var nestedDir in Directory.GetDirectories(pluginsDir))
        {
            foreach (var file in Directory.GetFiles(nestedDir))
            {
                var fileName = Path.GetFileName(file);
                var destination = Path.Combine(pluginsDir, fileName);

                if (File.Exists(destination))
                    File.Delete(destination);
                File.Move(file, destination);

                if (fileName.EndsWith(DataXmlSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var assemblyFileName = fileName[..^DataXmlSuffix.Length];
                    RewriteFileName(destination, $"/{PluginAssembliesDir}/{assemblyFileName}");
                    normalized.Add(assemblyFileName);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(nestedDir).Any())
                Directory.Delete(nestedDir);
        }

        return normalized;
    }

    public static IReadOnlyList<string> ExcludeProjectReferenceBinaries(
        string unpackedRoot,
        IReadOnlyCollection<string> referencedAssemblyNames)
    {
        var excluded = new List<string>();
        var pluginsDir = Path.Combine(unpackedRoot, PluginAssembliesDir);
        if (!Directory.Exists(pluginsDir) || referencedAssemblyNames.Count == 0)
            return excluded;

        foreach (var dataXml in Directory.GetFiles(pluginsDir, "*" + DataXmlSuffix))
        {
            var assemblySimpleName = ReadAssemblySimpleName(dataXml);
            if (assemblySimpleName is null || !MatchesReference(assemblySimpleName, referencedAssemblyNames))
                continue;

            var dllName = Path.GetFileName(dataXml)[..^DataXmlSuffix.Length];
            var dllPath = Path.Combine(pluginsDir, dllName);
            if (File.Exists(dllPath))
            {
                File.Delete(dllPath);
                excluded.Add(dllName);
            }
        }

        return excluded;
    }

    public static IReadOnlyList<string> ExcludeScriptLibraryWebResources(
        string unpackedRoot,
        IReadOnlyCollection<string> webResourceNames)
    {
        var excluded = new List<string>();
        var webResDir = Path.Combine(unpackedRoot, WebResourcesDir);
        if (!Directory.Exists(webResDir) || webResourceNames.Count == 0)
            return excluded;

        foreach (var dataXml in Directory.GetFiles(webResDir, "*" + DataXmlSuffix))
        {
            var resourceName = Path.GetFileName(dataXml)[..^DataXmlSuffix.Length];
            if (!webResourceNames.Contains(resourceName))
                continue;

            var contentPath = Path.Combine(webResDir, resourceName);
            if (File.Exists(contentPath))
            {
                File.Delete(contentPath);
                excluded.Add(resourceName);
            }
        }

        return excluded;
    }

    private static string? ReadAssemblySimpleName(string dataXmlPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(dataXmlPath);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var fullName = doc.Root?.Attribute("FullName")?.Value;
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var comma = fullName.IndexOf(',');
        return (comma >= 0 ? fullName[..comma] : fullName).Trim();
    }

    private static bool MatchesReference(string assemblySimpleName, IReadOnlyCollection<string> referencedNames)
    {
        foreach (var name in referencedNames)
        {
            if (assemblySimpleName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
            if (assemblySimpleName.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.EndsWith("." + assemblySimpleName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void RewriteFileName(string dataXmlPath, string newFileName)
    {
        var doc = XDocument.Load(dataXmlPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var fileNameElement = doc.Descendants(ns + "FileName").FirstOrDefault();
        if (fileNameElement is null || fileNameElement.Value == newFileName)
            return;

        fileNameElement.Value = newFileName;
        doc.Save(dataXmlPath);
    }
}
