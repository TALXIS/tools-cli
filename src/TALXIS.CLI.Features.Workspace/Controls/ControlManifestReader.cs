using System.IO.Compression;
using System.Xml.Linq;

namespace TALXIS.CLI.Features.Workspace.Controls;

/// <summary>
/// Locates and parses a PCF <c>ControlManifest.xml</c> from a bare file or from inside
/// a control distribution archive (solution zip, Package Deployer pdpkg.zip, or NuGet nupkg —
/// archives are searched recursively, so a nupkg wrapping a pdpkg wrapping a solution works).
/// When the manifest comes from a solution archive, the publisher-prefixed control name
/// (e.g. <c>talxis_TALXIS.PCF.Grid</c>) is resolved from the solution's <c>customizations.xml</c>.
/// </summary>
public static class ControlManifestReader
{
    private const int MaxArchiveDepth = 3;

    public static ControlManifestInfo Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Manifest source not found: {path}");

        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(path);
            return ParseManifest(stream);
        }

        using var archiveStream = File.OpenRead(path);
        var result = SearchArchive(archiveStream, MaxArchiveDepth);
        if (result.Manifest == null)
            throw new InvalidOperationException($"No ControlManifest.xml found inside '{path}' (searched nested archives up to {MaxArchiveDepth} levels).");

        result.Manifest.PrefixedName = ResolvePrefixedName(result);
        return result.Manifest;
    }

    private sealed class ArchiveSearchResult
    {
        public ControlManifestInfo? Manifest { get; set; }
        /// <summary>The archive entry path the manifest was found at (e.g. Controls/talxis_TALXIS.PCF.Grid/ControlManifest.xml).</summary>
        public string? ManifestEntryPath { get; set; }
        /// <summary>CustomControl names declared in customizations.xml of the same solution archive.</summary>
        public List<string> CustomControlNames { get; } = [];
    }

    private static ArchiveSearchResult SearchArchive(Stream stream, int depthBudget)
    {
        var result = new ArchiveSearchResult();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Equals("ControlManifest.xml", StringComparison.OrdinalIgnoreCase) && result.Manifest == null)
            {
                using var entryStream = entry.Open();
                result.Manifest = ParseManifest(entryStream);
                result.ManifestEntryPath = entry.FullName;
            }
            else if (entry.Name.Equals("customizations.xml", StringComparison.OrdinalIgnoreCase))
            {
                using var entryStream = entry.Open();
                result.CustomControlNames.AddRange(ReadCustomControlNames(entryStream));
            }
        }

        if (result.Manifest != null || depthBudget <= 0)
            return result;

        foreach (var entry in archive.Entries)
        {
            if (!IsNestedArchive(entry.Name))
                continue;
            using var entryStream = entry.Open();
            using var buffered = CopyToMemory(entryStream);
            var nested = SearchArchive(buffered, depthBudget - 1);
            if (nested.Manifest != null)
                return nested;
        }

        return result;
    }

    private static bool IsNestedArchive(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

    // ZipArchive entry streams are not seekable, but nested ZipArchive needs a seekable stream.
    private static MemoryStream CopyToMemory(Stream source)
    {
        var memory = new MemoryStream();
        source.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    private static string? ResolvePrefixedName(ArchiveSearchResult result)
    {
        if (result.Manifest == null)
            return null;

        // The solution declares the prefixed name; match on the qualified-name suffix.
        var suffix = "_" + result.Manifest.QualifiedName;
        var fromCustomizations = result.CustomControlNames.FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (fromCustomizations != null)
            return fromCustomizations;

        // Fall back to the folder name in the solution layout (Controls/<prefixed-name>/ControlManifest.xml).
        var folder = Path.GetDirectoryName(result.ManifestEntryPath)?.Replace('\\', '/').Split('/').LastOrDefault();
        if (!string.IsNullOrEmpty(folder) && folder.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return folder;

        return null;
    }

    private static IEnumerable<string> ReadCustomControlNames(Stream customizationsStream)
    {
        var doc = XDocument.Load(customizationsStream);
        return doc.Descendants("CustomControl")
            .Select(c => c.Element("Name")?.Value)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();
    }

    private static ControlManifestInfo ParseManifest(Stream stream)
    {
        var doc = XDocument.Load(stream);
        var control = doc.Descendants("control").FirstOrDefault()
            ?? throw new InvalidOperationException("Invalid control manifest: no <control> element.");

        var ns = control.Attribute("namespace")?.Value
            ?? throw new InvalidOperationException("Invalid control manifest: <control> has no 'namespace' attribute.");
        var constructor = control.Attribute("constructor")?.Value
            ?? throw new InvalidOperationException("Invalid control manifest: <control> has no 'constructor' attribute.");

        var properties = control.Elements("property")
            .Select(p => new ControlManifestProperty
            {
                Name = p.Attribute("name")?.Value ?? "",
                OfType = p.Attribute("of-type")?.Value ?? p.Attribute("of-type-group")?.Value ?? "SingleLine.Text",
                DefaultValue = p.Attribute("default-value")?.Value,
                Required = string.Equals(p.Attribute("required")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                EnumValues = p.Elements("value").Select(v => v.Value.Trim()).ToList(),
            })
            .Where(p => p.Name.Length > 0)
            .ToList();

        return new ControlManifestInfo
        {
            Namespace = ns,
            Constructor = constructor,
            Version = control.Attribute("version")?.Value,
            DataSets = control.Elements("data-set").Select(d => d.Attribute("name")?.Value ?? "").Where(n => n.Length > 0).ToList(),
            Properties = properties,
        };
    }
}
