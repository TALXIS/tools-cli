using System.Xml.Linq;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline;

internal static class SolutionPullPipelineXml
{
    public static XElement? FindSolutionManifest(XDocument document)
        => document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "SolutionManifest");

    public static string? ReadSolutionManifestElementValue(string root, string elementName)
    {
        var solutionXml = Path.Combine(root, SolutionPullPipelineConstants.OtherDirectoryName, "Solution.xml");
        if (!File.Exists(solutionXml))
            return null;

        try
        {
            var document = XDocument.Load(solutionXml);
            var manifest = FindSolutionManifest(document);
            return manifest?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == elementName)?
                .Value
                .Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    public static string? ReadPublisherPrefix(string stagingRoot)
    {
        var solutionXml = Path.Combine(stagingRoot, SolutionPullPipelineConstants.OtherDirectoryName, "Solution.xml");
        if (!File.Exists(solutionXml))
            return null;

        try
        {
            var document = XDocument.Load(solutionXml);
            return document.Descendants("CustomizationPrefix").FirstOrDefault()?.Value?.Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    public static string? ReadAssemblySimpleName(string dataXmlPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(dataXmlPath);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var fullName = document.Root?.Attribute("FullName")?.Value;
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var comma = fullName.IndexOf(',');
        return (comma >= 0 ? fullName[..comma] : fullName).Trim();
    }

    public static string? ReadFileNameElement(string dataXmlPath)
    {
        try
        {
            var document = XDocument.Load(dataXmlPath);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;
            return document.Descendants(ns + "FileName").FirstOrDefault()?.Value?.Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    public static void RewriteFileName(string dataXmlPath, string newFileName)
    {
        var document = XDocument.Load(dataXmlPath);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var fileNameElement = document.Descendants(ns + "FileName").FirstOrDefault();
        if (fileNameElement is null || fileNameElement.Value == newFileName)
            return;

        fileNameElement.Value = newFileName;
        document.Save(dataXmlPath);
    }
}
