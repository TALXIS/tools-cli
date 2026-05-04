using System.Xml.Linq;

namespace TALXIS.CLI.Features.Workspace.Localization;

public static class LocalizationWriter
{
    public sealed record WriteResult(int Added, int Updated, int Skipped, List<string> Errors);

    public static WriteResult Apply(string workspaceRoot, TranslationFile file)
    {
        int added = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        var grouped = file.Strings
            .Where(s => !string.IsNullOrEmpty(s.Target))
            .GroupBy(s => s.File);

        foreach (var group in grouped)
        {
            var path = Path.Combine(workspaceRoot, group.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(path))
            {
                errors.Add($"File not found: {group.Key}");
                continue;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load {group.Key}: {ex.Message}");
                continue;
            }

            bool docChanged = false;
            foreach (var unit in group)
            {
                var source = LocalizationScanner.LocateByXPath(doc, unit.XPath);
                if (source == null)
                {
                    errors.Add($"Could not locate element for id={unit.Id} at {unit.File}{unit.XPath}");
                    skipped++;
                    continue;
                }

                var existing = FindExistingTranslation(source, unit.LanguageAttr, file.TargetLanguage);
                if (existing != null)
                {
                    if (SetValue(existing, unit.ValueAttr, unit.Target!))
                    {
                        updated++;
                        docChanged = true;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                else
                {
                    var clone = new XElement(source);
                    var langAttr = clone.Attribute(unit.LanguageAttr);
                    if (langAttr != null)
                    {
                        langAttr.Value = file.TargetLanguage;
                    }
                    SetValue(clone, unit.ValueAttr, unit.Target!);
                    InsertSiblingPreservingIndent(source, clone);
                    added++;
                    docChanged = true;
                }
            }

            if (docChanged)
            {
                try
                {
                    doc.Save(path, SaveOptions.DisableFormatting);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to save {group.Key}: {ex.Message}");
                }
            }
        }

        return new WriteResult(added, updated, skipped, errors);
    }

    /// <summary>
    /// Inserts <paramref name="clone"/> immediately after <paramref name="source"/>,
    /// mirroring the whitespace prefix that decorates <paramref name="source"/> so
    /// the new sibling sits on its own indented line. If <paramref name="source"/>
    /// is not preceded by newline-containing whitespace (single-line layout),
    /// falls back to a plain inline insert to preserve the existing style.
    /// </summary>
    private static void InsertSiblingPreservingIndent(XElement source, XElement clone)
    {
        var leading = (source.PreviousNode as XText)?.Value;
        if (!string.IsNullOrEmpty(leading) && leading.IndexOfAny(new[] { '\n', '\r' }) >= 0)
        {
            // Replicate the exact whitespace (newline + indent) so the writer
            // honours it under SaveOptions.DisableFormatting.
            source.AddAfterSelf(new XText(leading), clone);
        }
        else
        {
            source.AddAfterSelf(clone);
        }
    }

    private static XElement? FindExistingTranslation(XElement source, string languageAttr, string targetLcid)
    {
        var parent = source.Parent;
        if (parent == null) return null;
        foreach (var sibling in parent.Elements(source.Name))
        {
            if (ReferenceEquals(sibling, source)) continue;
            var attr = sibling.Attribute(languageAttr);
            if (attr != null && attr.Value == targetLcid) return sibling;
        }
        return null;
    }

    private static bool SetValue(XElement el, string? valueAttr, string value)
    {
        if (valueAttr != null)
        {
            var attr = el.Attribute(valueAttr);
            if (attr == null)
            {
                el.SetAttributeValue(valueAttr, value);
                return true;
            }
            if (attr.Value == value) return false;
            attr.Value = value;
            return true;
        }

        if (el.Value == value) return false;
        el.Value = value;
        return true;
    }

    public sealed record AddLanguageResult(int FilesTouched, int Already);

    public static AddLanguageResult AddLanguageToCustomizations(string workspaceRoot, string lcid)
    {
        int touched = 0, already = 0;
        foreach (var file in LocalizationScanner.EnumerateXmlFiles(workspaceRoot))
        {
            if (!Path.GetFileName(file).Equals("Customizations.xml", StringComparison.OrdinalIgnoreCase))
                continue;

            XDocument doc;
            try { doc = XDocument.Load(file, LoadOptions.PreserveWhitespace); }
            catch { continue; }
            if (doc.Root == null) continue;

            var languages = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Languages");
            if (languages == null) continue;

            var existing = languages.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Language" && e.Value == lcid);
            if (existing != null) { already++; continue; }

            var template = languages.Elements().FirstOrDefault(e => e.Name.LocalName == "Language");
            var newLang = template != null
                ? new XElement(template.Name, lcid)
                : new XElement("Language", lcid);
            languages.Add(newLang);

            doc.Save(file, SaveOptions.DisableFormatting);
            touched++;
        }
        return new AddLanguageResult(touched, already);
    }
}
