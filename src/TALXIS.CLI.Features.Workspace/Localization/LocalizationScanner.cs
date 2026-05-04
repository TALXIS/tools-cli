using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace TALXIS.CLI.Features.Workspace.Localization;

public sealed record LocalizableSite(
    string FileRelativePath,
    string XPath,
    string LanguageAttr,
    string? ValueAttr,
    string Source,
    string Id);

public static class LocalizationScanner
{
    private static readonly string[] IgnoredDirs = new[]
    {
        ".git", "bin", "obj", "node_modules", ".vs", ".idea", "packages"
    };

    public static IEnumerable<string> EnumerateXmlFiles(string workspaceRoot)
    {
        foreach (var path in Directory.EnumerateFiles(workspaceRoot, "*.xml", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
            if (IgnoredDirs.Any(d => rel.Split('/').Contains(d, StringComparer.OrdinalIgnoreCase)))
                continue;
            yield return path;
        }
    }

    public static IEnumerable<LocalizableSite> Scan(string workspaceRoot, string sourceLcid)
    {
        foreach (var file in EnumerateXmlFiles(workspaceRoot))
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                continue;
            }

            if (doc.Root == null) continue;

            var rel = Path.GetRelativePath(workspaceRoot, file).Replace('\\', '/');
            foreach (var el in doc.Descendants())
            {
                var (langAttr, langValue) = GetLanguageAttribute(el);
                if (langAttr == null || langValue != sourceLcid) continue;

                var valueAttr = GetValueAttributeName(el);
                string source;
                if (valueAttr != null)
                {
                    source = el.Attribute(valueAttr)?.Value ?? string.Empty;
                }
                else
                {
                    source = el.Nodes().OfType<XText>().FirstOrDefault()?.Value ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(source)) continue;
                if (valueAttr == null) continue;

                var xpath = BuildXPath(el);
                var id = MakeId(rel, xpath);

                yield return new LocalizableSite(
                    rel,
                    xpath,
                    langAttr,
                    valueAttr,
                    source,
                    id);
            }
        }
    }

    public static (string? Name, string? Value) GetLanguageAttribute(XElement el)
    {
        foreach (var attr in el.Attributes())
        {
            if (attr.Name.LocalName.Equals("languagecode", StringComparison.OrdinalIgnoreCase) ||
                attr.Name.LocalName.Equals("LCID", StringComparison.OrdinalIgnoreCase))
            {
                return (attr.Name.LocalName, attr.Value);
            }
        }
        return (null, null);
    }

    public static string? GetValueAttributeName(XElement el)
    {
        if (el.Attribute("description") != null) return "description";
        if (el.Attribute("Title") != null) return "Title";
        return null;
    }

    public static string BuildXPath(XElement el)
    {
        var parts = new List<string>();
        XElement? current = el;
        while (current != null)
        {
            var parent = current.Parent;
            int index = 1;
            if (parent != null)
            {
                var siblings = parent.Elements(current.Name).ToList();
                index = siblings.IndexOf(current) + 1;
            }
            parts.Insert(0, $"{current.Name.LocalName}[{index}]");
            current = parent;
        }
        return "/" + string.Join("/", parts);
    }

    public static string MakeId(string fileRel, string xpath)
    {
        var bytes = Encoding.UTF8.GetBytes(fileRel + "|" + xpath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).Substring(0, 12).ToLowerInvariant();
    }

    public static XElement? LocateByXPath(XDocument doc, string xpath)
    {
        var segments = xpath.TrimStart('/').Split('/');
        XElement? current = doc.Root;
        if (current == null) return null;

        var first = ParseSegment(segments[0]);
        if (!current.Name.LocalName.Equals(first.tag, StringComparison.Ordinal)) return null;

        for (int i = 1; i < segments.Length; i++)
        {
            if (current == null) return null;
            var (tag, index) = ParseSegment(segments[i]);
            var matching = current.Elements().Where(e => e.Name.LocalName.Equals(tag, StringComparison.Ordinal)).ToList();
            if (index - 1 < 0 || index - 1 >= matching.Count) return null;
            current = matching[index - 1];
        }
        return current;
    }

    private static (string tag, int index) ParseSegment(string segment)
    {
        var bracket = segment.IndexOf('[');
        if (bracket < 0) return (segment, 1);
        var tag = segment.Substring(0, bracket);
        var idx = int.Parse(segment.Substring(bracket + 1, segment.Length - bracket - 2));
        return (tag, idx);
    }
}
