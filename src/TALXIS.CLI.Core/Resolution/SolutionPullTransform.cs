using System.Xml.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Core.Resolution;

public static class SolutionPullTransform
{
    private const string PluginAssembliesDir = "PluginAssemblies";
    private const string WebResourcesDir = "WebResources";
    private const string DataXmlSuffix = ".data.xml";
    private static readonly Regex StandardSystemRelationshipPattern = new(
        "^(business_unit_.+|lk_.+_(createdby|modifiedby)|owner_.+|team_.+|user_.+)$",
        RegexOptions.IgnoreCase);

    public static void NormalizeSolutionManifest(string stagingRoot, string destinationRoot)
    {
        var stagingSolutionXml = Path.Combine(stagingRoot, "Other", "Solution.xml");
        if (!File.Exists(stagingSolutionXml))
            return;

        XDocument stagingDocument;
        try
        {
            stagingDocument = XDocument.Load(stagingSolutionXml);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        var stagingManifest = FindSolutionManifest(stagingDocument);
        if (stagingManifest is null)
            return;

        var namespaceName = stagingManifest.Name.Namespace;
        var stagingVersion = stagingManifest.Element(namespaceName + "Version");
        var localVersion = ReadSolutionManifestElementValue(destinationRoot, "Version");
        if (stagingVersion is not null && !string.IsNullOrWhiteSpace(localVersion))
            stagingVersion.Value = localVersion;

        var managedElement = stagingManifest.Element(namespaceName + "Managed");
        if (managedElement is null)
        {
            managedElement = new XElement(namespaceName + "Managed");
            stagingManifest.Add(managedElement);
        }

        managedElement.Value = "2";
        stagingDocument.Save(stagingSolutionXml);
    }

    public static IReadOnlyList<string> ExcludeStandardSystemRelationships(string stagingRoot)
    {
        var relationshipsXml = Path.Combine(stagingRoot, "Other", "Relationships.xml");
        if (!File.Exists(relationshipsXml))
            return [];

        var fileContents = File.ReadAllText(relationshipsXml);
        if (string.IsNullOrWhiteSpace(fileContents))
            return [];

        XDocument relationshipsDocument;
        try
        {
            relationshipsDocument = XDocument.Parse(fileContents);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var removed = relationshipsDocument
            .Descendants()
            .Where(element => element.Name.LocalName == "EntityRelationship")
            .Select(element => new
            {
                Element = element,
                Name = element.Attribute("Name")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && StandardSystemRelationshipPattern.IsMatch(item.Name))
            .ToList();

        if (removed.Count == 0)
            return [];

        foreach (var relationship in removed)
            relationship.Element.Remove();

        relationshipsDocument.Save(relationshipsXml);
        return removed.Select(item => item.Name!).ToArray();
    }

    /// <summary>
    /// Rearranges plugin assembly files in <paramref name="stagingRoot"/> to match the
    /// path conventions already established in <paramref name="destinationRoot"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dataverse always exports assemblies into <c>PluginAssemblies/{Name}-{GUID}/</c>
    /// regardless of the layout stored in source control. This method uses the existing
    /// local <c>.data.xml</c> files as ground truth: it matches each staging assembly to
    /// its local counterpart by simple assembly name (the first segment of <c>FullName</c>),
    /// then moves the staging files to mirror the local directory structure and preserves
    /// the local <c>&lt;FileName&gt;</c> element value.
    /// </para>
    /// <para>
    /// Assemblies that do not yet exist in the destination (first sync or genuinely new) are
    /// placed flat under <c>PluginAssemblies/</c> and get a default
    /// <c>&lt;FileName&gt;/PluginAssemblies/{name}.dll&lt;/FileName&gt;</c> written — the TALXIS SDK
    /// convention. All empty sub-directories left behind are removed.
    /// </para>
    /// </remarks>
    /// <returns>Simple names of all assemblies that were repositioned.</returns>
    public static IReadOnlyList<string> RestoreLocalFileNameConventions(
        string stagingRoot,
        string destinationRoot,
        ILogger? logger = null)
    {
        var restored = new List<string>();
        var stagingPluginsDir = Path.Combine(stagingRoot, PluginAssembliesDir);
        if (!Directory.Exists(stagingPluginsDir))
            return restored;

        // Build map: assembly simple name → (relative sub-dir, local FileName value)
        var localConventionMap = BuildLocalPluginConventionMap(destinationRoot);

        foreach (var nestedDir in Directory.GetDirectories(stagingPluginsDir).ToList())
        {
            // Identify the assembly in this folder via its .data.xml
            var dataXmlFile = Directory.GetFiles(nestedDir, "*" + DataXmlSuffix).FirstOrDefault();
            if (dataXmlFile is null)
                continue;

            var simpleName = ReadAssemblySimpleName(dataXmlFile);
            if (simpleName is null)
                continue;

            // Determine target sub-directory and desired <FileName> value.
            string targetDir;
            string? desiredFileName;
            bool isNew = false;
            if (localConventionMap.TryGetValue(simpleName, out var localInfo))
            {
                targetDir = localInfo.RelativeDir == "."
                    ? stagingPluginsDir
                    : Path.Combine(stagingPluginsDir, localInfo.RelativeDir);
                desiredFileName = localInfo.LocalFileName;
            }
            else
            {
                // New assembly — apply TALXIS SDK flat default.
                targetDir = stagingPluginsDir;
                desiredFileName = null; // will be derived below
                isNew = true;
                logger?.LogInformation(
                    "Assembly '{Name}' is new (no local convention found) — " +
                    "using flat PluginAssemblies/ layout as default.", simpleName);
            }

            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(nestedDir))
            {
                var fileName = Path.GetFileName(file);
                var destination = Path.Combine(targetDir, fileName);

                if (!string.Equals(file, destination, StringComparison.Ordinal))
                {
                    if (File.Exists(destination))
                    {
                        logger?.LogWarning(
                            "Staging file '{Destination}' already exists — overwriting. " +
                            "This may indicate two assemblies with the same name in different folders.",
                            destination);
                        File.Delete(destination);
                    }

                    File.Move(file, destination);
                }

                if (fileName.EndsWith(DataXmlSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    // For new assemblies write the TALXIS SDK flat <FileName> default.
                    // For known assemblies restore the local <FileName> so the value is
                    // consistent with the destination's established convention.
                    var targetFileName = isNew
                        ? $"/{PluginAssembliesDir}/{fileName[..^DataXmlSuffix.Length]}"
                        : desiredFileName;

                    if (targetFileName is not null)
                        RewriteFileName(destination, targetFileName);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(nestedDir).Any())
                Directory.Delete(nestedDir);

            restored.Add(simpleName);
        }

        return restored;
    }

    public static IReadOnlyList<string> ExcludeProjectReferenceBinaries(
        string unpackedRoot,
        IReadOnlyCollection<string> referencedAssemblyNames)
    {
        var excluded = new List<string>();
        var pluginsDir = Path.Combine(unpackedRoot, PluginAssembliesDir);
        if (!Directory.Exists(pluginsDir) || referencedAssemblyNames.Count == 0)
            return excluded;

        // Search recursively: after RestoreLocalFileNameConventions the .data.xml files may
        // live flat in PluginAssemblies/ or inside sub-directories depending on local convention.
        foreach (var dataXml in Directory.GetFiles(pluginsDir, "*" + DataXmlSuffix, SearchOption.AllDirectories))
        {
            var assemblySimpleName = ReadAssemblySimpleName(dataXml);
            if (assemblySimpleName is null || !MatchesReference(assemblySimpleName, referencedAssemblyNames))
                continue;

            // Binary sits alongside the .data.xml regardless of nesting depth.
            var binaryName = Path.GetFileName(dataXml)[..^DataXmlSuffix.Length];
            var binaryPath = Path.Combine(Path.GetDirectoryName(dataXml)!, binaryName);
            if (File.Exists(binaryPath))
            {
                File.Delete(binaryPath);
                excluded.Add(binaryName);
            }
        }

        return excluded;
    }

    private const string ControlsDir = "Controls";

    /// <summary>
    /// Deletes PCF control directories from the staging <c>Controls/</c> folder for controls
    /// that are built locally from project references (so their output should not be committed).
    /// </summary>
    /// <remarks>
    /// Dataverse names control folders as <c>{publisherPrefix}_{Namespace}_{ControlName}</c>
    /// (dots replaced by underscores). The publisher prefix is read from
    /// <c>Other/Solution.xml</c> in the staging root.
    /// </remarks>
    /// <param name="stagingRoot">Root of the unpacked staging directory.</param>
    /// <param name="pcfControlNames">
    /// Fully-qualified PCF names, e.g. <c>"UdppControls.QuantityIndicator"</c>,
    /// as returned by <see cref="ProjectReferenceReader.ReadPcfControlNames"/>.
    /// </param>
    /// <returns>Names of deleted control folders.</returns>
    public static IReadOnlyList<string> ExcludePcfControls(
        string stagingRoot,
        IReadOnlyCollection<string> pcfControlNames)
    {
        var excluded = new List<string>();
        var controlsDir = Path.Combine(stagingRoot, ControlsDir);
        if (!Directory.Exists(controlsDir) || pcfControlNames.Count == 0)
            return excluded;

        var publisherPrefix = ReadPublisherPrefix(stagingRoot);
        if (string.IsNullOrWhiteSpace(publisherPrefix))
            return excluded;

        foreach (var controlDir in Directory.GetDirectories(controlsDir))
        {
            var dirName = Path.GetFileName(controlDir);
            if (MatchesPcfControl(dirName, pcfControlNames, publisherPrefix))
            {
                Directory.Delete(controlDir, recursive: true);
                excluded.Add(dirName);
            }
        }

        return excluded;
    }

    /// <summary>
    /// Reads <c>&lt;CustomizationPrefix&gt;</c> from <c>Other/Solution.xml</c> in the staging root.
    /// This is the publisher prefix Dataverse uses when naming PCF control folders.
    /// </summary>
    private static string? ReadPublisherPrefix(string stagingRoot)
    {
        var solutionXml = Path.Combine(stagingRoot, "Other", "Solution.xml");
        if (!File.Exists(solutionXml))
            return null;
        try
        {
            var doc = XDocument.Load(solutionXml);
            return doc.Descendants("CustomizationPrefix").FirstOrDefault()?.Value?.Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static XElement? FindSolutionManifest(XDocument document)
        => document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "SolutionManifest");

    private static string? ReadSolutionManifestElementValue(string root, string elementName)
    {
        var solutionXml = Path.Combine(root, "Other", "Solution.xml");
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

    /// <summary>
    /// Checks whether a control folder name (e.g. <c>"udpp_UdppControls_QuantityIndicator"</c>)
    /// corresponds to any of the locally-referenced PCF controls.
    /// Folder name pattern: <c>{prefix}_{namespace_dots_replaced}_{name}</c>.
    /// </summary>
    private static bool MatchesPcfControl(
        string folderName,
        IReadOnlyCollection<string> pcfControlNames,
        string publisherPrefix)
    {
        foreach (var qualifiedName in pcfControlNames)
        {
            // "UdppControls.QuantityIndicator" → "UdppControls_QuantityIndicator"
            var namePart = qualifiedName.Replace('.', '_');
            // Expected folder: "udpp_UdppControls_QuantityIndicator"
            var expected = $"{publisherPrefix}_{namePart}";
            if (string.Equals(folderName, expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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

    /// <summary>
    /// Builds a map of assembly simple name → (relative sub-directory, local FileName value)
    /// for all assemblies already present in <paramref name="destinationRoot"/>.
    /// Relative directory is <c>"."</c> for flat-root files.
    /// </summary>
    private static Dictionary<string, (string RelativeDir, string? LocalFileName)> BuildLocalPluginConventionMap(string destinationRoot)
    {
        var map = new Dictionary<string, (string RelativeDir, string? LocalFileName)>(StringComparer.OrdinalIgnoreCase);
        var localPluginsDir = Path.Combine(destinationRoot, PluginAssembliesDir);
        if (!Directory.Exists(localPluginsDir))
            return map;

        foreach (var dataXml in Directory.GetFiles(localPluginsDir, "*" + DataXmlSuffix, SearchOption.AllDirectories))
        {
            var simpleName = ReadAssemblySimpleName(dataXml);
            if (simpleName is null)
                continue;

            var dataXmlDir = Path.GetDirectoryName(dataXml)!;
            var relativeDir = Path.GetRelativePath(localPluginsDir, dataXmlDir);
            var localFileName = ReadFileNameElement(dataXml);
            // Path.GetRelativePath returns "." when the file is directly in localPluginsDir.
            map[simpleName] = (relativeDir, localFileName);
        }

        return map;
    }

    private static string? ReadFileNameElement(string dataXmlPath)
    {
        try
        {
            var doc = XDocument.Load(dataXmlPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            return doc.Descendants(ns + "FileName").FirstOrDefault()?.Value?.Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
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
            // Exact match: assembly simple name == referenced project assembly name.
            if (assemblySimpleName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;

            // Namespace-prefix match: the assembly registered in Dataverse carries a namespace
            // prefix not present in the project's AssemblyName. E.g. the project defines
            // <AssemblyName>Logic</AssemblyName> but the assembly full name starts with
            // "Acme.Apps.Logic, ...".
            if (assemblySimpleName.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                return true;

            // NOTE: the inverse direction — name.EndsWith("." + assemblySimpleName) — was
            // intentionally removed. It caused false positives: a third-party assembly named
            // "Logic" would match a referenced project "Acme.Apps.MoveOrder.Logic" because
            // "Acme.Apps.MoveOrder.Logic".EndsWith(".Logic") == true, permanently deleting
            // an unrelated binary from staging.
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
