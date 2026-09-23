using System.Xml.Linq;
using TALXIS.CLI.Core;
using TALXIS.Platform.Metadata;
using TALXIS.Platform.Metadata.Components;
using TALXIS.Platform.Metadata.Merging;
using TALXIS.Platform.Metadata.Serialization.Xml;

namespace TALXIS.CLI.Features.Workspace;

/// <summary>
/// Pure projection logic for <see cref="ComponentInspectCliCommand"/> -
/// turns metadata objects into serializable inspection results.
/// </summary>
internal static class ComponentInspectHelpers
{
    private const int EnglishLanguageCode = 1033;

    public static FormInspectionResult BuildFormResult(FormMetadata form, int? depth, string? uniqueName = null)
    {
        var tabs = new List<FormTabNode>();
        foreach (var tab in FindDescendants(form.Body, "tab"))
        {
            List<FormSectionNode>? sections = null;
            if (depth is null || depth >= 2)
            {
                sections = new List<FormSectionNode>();
                foreach (var section in FindDescendants(tab, "section"))
                {
                    List<FormControlNode>? controls = null;
                    if (depth is null || depth >= 3)
                    {
                        controls = new List<FormControlNode>();
                        foreach (var cell in FindDescendants(section, "cell"))
                        {
                            var cellLabel = PickNodeLabel(cell);
                            foreach (var control in FindDescendants(cell, "control"))
                            {
                                controls.Add(new FormControlNode(
                                    control.GetAttribute("id"),
                                    control.GetAttribute("datafieldname"),
                                    control.GetAttribute("classid"),
                                    cellLabel));
                            }
                        }
                    }

                    sections.Add(new FormSectionNode(
                        section.GetAttribute("id"),
                        section.GetAttribute("name"),
                        PickNodeLabel(section),
                        controls));
                }
            }

            tabs.Add(new FormTabNode(
                tab.GetAttribute("id"),
                tab.GetAttribute("name"),
                PickNodeLabel(tab),
                sections));
        }

        return new FormInspectionResult(
            form.FormId,
            form.FormType,
            form.EntityLogicalName,
            PickLabel(form.DisplayName),
            PickLabel(form.Description),
            uniqueName,
            tabs);
    }

    /// <summary>
    /// Parses a dialog component (Dialogs/{guid}.xml stored as a generic component) into
    /// form metadata so dialogs can be inspected the same way as entity forms.
    /// Returns null when the content is missing or not a dialog document.
    /// </summary>
    public static DialogFormInfo? ParseDialogForm(GenericComponentMetadata component)
    {
        if (string.IsNullOrWhiteSpace(component.SerializedContent))
            return null;

        XElement root;
        try
        {
            root = XElement.Parse(component.SerializedContent);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        if (!string.Equals(root.Name.LocalName, "Dialog", StringComparison.OrdinalIgnoreCase))
            return null;

        var formsElement = root.Element("FormXml")?.Element("forms");
        var formElement = formsElement?.Element("form");

        var form = new FormMetadata
        {
            FormId = root.Element("FormId")?.Value ?? component.Id,
            FormType = formsElement?.Attribute("type")?.Value ?? "dialog",
            DisplayName = ParseLocalizedLabel(root.Element("LocalizedNames"), "LocalizedName"),
            Description = ParseLocalizedLabel(root.Element("Descriptions"), "Description"),
            Body = formElement is null ? null : MergeableNodeXmlConverter.FromXElement(formElement),
        };

        return new DialogFormInfo(form, root.Element("UniqueName")?.Value ?? component.Name);
    }

    public static ViewInspectionResult BuildViewResult(SavedQueryMetadata view)
    {
        // The metadata library exposes layoutxml/fetchxml as element text (empty for
        // nested XML), so read the grid and fetch definitions from the source file.
        var savedQuery = TryLoadSavedQueryElement(view);
        var layout = savedQuery?.Element("layoutxml")?.Elements().FirstOrDefault();
        var fetch = savedQuery?.Element("fetchxml")?.Elements().FirstOrDefault();
        var isQuickFind = view.IsQuickFindQuery || savedQuery?.Element("isquickfindquery")?.Value == "1";

        return new ViewInspectionResult(
            view.SavedQueryId,
            PickLabel(view.DisplayName),
            view.EntityLogicalName,
            view.QueryType,
            view.IsDefault,
            isQuickFind,
            ParseLayoutColumns(layout),
            ParseFetchOrder(fetch),
            fetch?.ToString());
    }

    private static XElement? TryLoadSavedQueryElement(SavedQueryMetadata view)
    {
        var path = view.Source?.FilePath;
        if (path is null || !File.Exists(path))
            return null;

        try
        {
            var queries = XDocument.Load(path).Root?.Elements("savedquery").ToList();
            if (queries is null || queries.Count == 0)
                return null;

            var normalizedId = NormalizeGuid(view.SavedQueryId);
            return queries.FirstOrDefault(q => NormalizeGuid(q.Element("savedqueryid")?.Value) == normalizedId)
                ?? (queries.Count == 1 ? queries[0] : null);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static Label? ParseLocalizedLabel(XElement? container, string elementName)
    {
        if (container is null)
            return null;

        var labels = container.Elements(elementName)
            .Select(e => (Text: e.Attribute("description")?.Value, Code: (int?)e.Attribute("languagecode")))
            .Where(l => l.Text is not null)
            .ToList();
        if (labels.Count == 0)
            return null;

        var picked = labels.FirstOrDefault(l => l.Code == EnglishLanguageCode);
        if (picked.Text is null)
            picked = labels[0];
        return new Label(picked.Text!, picked.Code ?? EnglishLanguageCode);
    }

    private static IReadOnlyList<ViewColumnNode> ParseLayoutColumns(XElement? grid)
        => grid is null
            ? []
            : grid.Descendants("cell")
                .Select(c => new ViewColumnNode(c.Attribute("name")?.Value, (int?)c.Attribute("width")))
                .ToList();

    private static IReadOnlyList<string> ParseFetchOrder(XElement? fetch)
        => fetch is null
            ? []
            : fetch.Descendants("order")
                .Select(o =>
                {
                    var attribute = o.Attribute("attribute")?.Value ?? "?";
                    return (bool?)o.Attribute("descending") == true ? $"{attribute} (desc)" : attribute;
                })
                .ToList();

    public static EntityInspectionResult BuildEntityResult(EntityMetadata entity)
    {
        var attributes = entity.Attributes
            .Select(a => new EntityAttributeNode(
                a.LogicalName,
                a.AttributeType.ToString(),
                PickLabel(a.DisplayName),
                a.RequiredLevel.ToString(),
                a.RequiredLevel is RequiredLevel.Required or RequiredLevel.ApplicationRequired or RequiredLevel.SystemRequired,
                a.IsCustomAttribute))
            .ToList();

        return new EntityInspectionResult(
            entity.LogicalName,
            entity.SchemaName,
            PickLabel(entity.DisplayName),
            entity.PrimaryIdAttribute,
            entity.PrimaryNameAttribute,
            entity.Ownership.ToString(),
            entity.IsCustomEntity,
            attributes);
    }

    /// <summary>
    /// Recursively finds all descendants with the given element name.
    /// Does not descend into a match - nested same-name elements are not expected in FormXml.
    /// </summary>
    public static IEnumerable<MergeableNode> FindDescendants(MergeableNode? node, string name)
    {
        if (node is null)
            yield break;

        foreach (var child in node.Children)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }
            else
            {
                foreach (var match in FindDescendants(child, name))
                    yield return match;
            }
        }
    }

    /// <summary>
    /// Reads a node's display label from its <c>labels/label@description</c> children,
    /// preferring English (1033) and falling back to the first label found.
    /// </summary>
    public static string? PickNodeLabel(MergeableNode node)
    {
        var labels = node.Children.FirstOrDefault(c => string.Equals(c.Name, "labels", StringComparison.OrdinalIgnoreCase));
        if (labels is null)
            return null;

        string? first = null;
        foreach (var label in labels.Children.Where(c => string.Equals(c.Name, "label", StringComparison.OrdinalIgnoreCase)))
        {
            var description = label.GetAttribute("description");
            if (description is null)
                continue;
            if (label.GetAttribute("languagecode") == EnglishLanguageCode.ToString())
                return description;
            first ??= description;
        }
        return first;
    }

    public static string? PickLabel(Label? label)
    {
        if (label is null)
            return null;
        if (label.LocalizedLabels.TryGetValue(EnglishLanguageCode, out var english))
            return english;
        return label.Default;
    }

    /// <summary>Normalizes a GUID string (with or without braces) for comparison; null if not a GUID.</summary>
    public static string? NormalizeGuid(string? value)
        => Guid.TryParse(value?.Trim('{', '}'), out var guid) ? guid.ToString("D") : null;

    public static void RenderEntityText(EntityInspectionResult entity)
    {
        OutputWriter.WriteLine($"Entity: {entity.LogicalName} ({entity.DisplayName ?? "no display name"})");
        OutputWriter.WriteLine($"Schema: {entity.SchemaName ?? "?"} | Ownership: {entity.Ownership} | Custom: {(entity.IsCustomEntity ? "yes" : "no")}");
        OutputWriter.WriteLine($"Primary id: {entity.PrimaryIdAttribute} | Primary name: {entity.PrimaryNameAttribute}");
        OutputWriter.WriteLine(string.Empty);

        if (entity.Attributes.Count == 0)
        {
            OutputWriter.WriteLine("No attributes found.");
            return;
        }

        int nameWidth = Math.Clamp(entity.Attributes.Max(a => a.LogicalName?.Length ?? 0), 12, 40);
        int typeWidth = Math.Clamp(entity.Attributes.Max(a => a.Type.Length), 4, 20);
        int requiredWidth = Math.Clamp(entity.Attributes.Max(a => RequiredDisplay(a).Length), 8, 20);

        string header = $"{"Logical Name".PadRight(nameWidth)} | {"Type".PadRight(typeWidth)} | {"Required".PadRight(requiredWidth)} | {"Custom",-6} | Display Name";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var attribute in entity.Attributes)
        {
            OutputWriter.WriteLine(
                $"{(attribute.LogicalName ?? "?").PadRight(nameWidth)} | " +
                $"{attribute.Type.PadRight(typeWidth)} | " +
                $"{RequiredDisplay(attribute).PadRight(requiredWidth)} | " +
                $"{(attribute.IsCustomAttribute ? "yes" : ""),-6} | " +
                $"{attribute.DisplayName ?? ""}");
        }
        OutputWriter.WriteLine($"\n{entity.Attributes.Count} attribute(s).");
    }

    private static string RequiredDisplay(EntityAttributeNode attribute)
        => attribute.RequiredLevel == nameof(RequiredLevel.None) ? string.Empty : attribute.RequiredLevel;

    public static void RenderViewText(ViewInspectionResult view)
    {
        OutputWriter.WriteLine($"View: {view.DisplayName ?? "unnamed"} {view.ViewId}");
        var flags = (view.IsDefault ? " | default" : "") + (view.IsQuickFindQuery ? " | quick find" : "");
        OutputWriter.WriteLine($"Entity: {view.EntityLogicalName ?? "?"} | Query type: {view.QueryType?.ToString() ?? "?"}{flags}");
        if (view.OrderBy.Count > 0)
            OutputWriter.WriteLine($"Order by: {string.Join(", ", view.OrderBy)}");
        OutputWriter.WriteLine(string.Empty);

        if (view.Columns.Count == 0)
        {
            OutputWriter.WriteLine("No columns found in layout.");
            return;
        }

        int nameWidth = Math.Clamp(view.Columns.Max(c => c.Name?.Length ?? 0), 6, 40);
        string header = $"{"Column".PadRight(nameWidth)} | Width";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var column in view.Columns)
            OutputWriter.WriteLine($"{(column.Name ?? "?").PadRight(nameWidth)} | {column.Width?.ToString() ?? ""}");
        OutputWriter.WriteLine($"\n{view.Columns.Count} column(s).");
    }

    public static void RenderFormText(FormInspectionResult form)
    {
        OutputWriter.WriteLine($"Form: {form.DisplayName ?? "unnamed"} {form.FormId}");
        var origin = form.EntityLogicalName is not null
            ? $"Entity: {form.EntityLogicalName}"
            : $"Unique name: {form.UniqueName ?? "?"}";
        OutputWriter.WriteLine($"Type: {form.FormType ?? "?"} | {origin}");
        OutputWriter.WriteLine(string.Empty);

        for (int t = 0; t < form.Tabs.Count; t++)
        {
            var tab = form.Tabs[t];
            bool lastTab = t == form.Tabs.Count - 1;
            OutputWriter.WriteLine($"{Branch(lastTab)}Tab: {FirstNonEmpty(tab.Label, tab.Name, tab.Id)}");

            var sections = tab.Sections;
            if (sections is null)
                continue;

            var tabIndent = Indent(lastTab);
            for (int s = 0; s < sections.Count; s++)
            {
                var section = sections[s];
                bool lastSection = s == sections.Count - 1;
                OutputWriter.WriteLine($"{tabIndent}{Branch(lastSection)}Section: {FirstNonEmpty(section.Label, section.Name, section.Id)}");

                var controls = section.Controls;
                if (controls is null)
                    continue;

                var sectionIndent = tabIndent + Indent(lastSection);
                for (int c = 0; c < controls.Count; c++)
                {
                    var control = controls[c];
                    bool lastControl = c == controls.Count - 1;
                    var label = !string.IsNullOrWhiteSpace(control.Label) ? $"  ({control.Label})" : string.Empty;
                    OutputWriter.WriteLine($"{sectionIndent}{Branch(lastControl)}{FirstNonEmpty(control.DataFieldName, control.Id)}{label}");
                }
            }
        }

        static string Branch(bool last) => last ? "└─ " : "├─ ";
        static string Indent(bool last) => last ? "   " : "│  ";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "?";
}

internal sealed record FormInspectionResult(
    string? FormId,
    string? FormType,
    string? EntityLogicalName,
    string? DisplayName,
    string? Description,
    string? UniqueName,
    IReadOnlyList<FormTabNode> Tabs);

/// <summary>A dialog parsed into form metadata plus its unique name (dialogs are not entity-bound).</summary>
internal sealed record DialogFormInfo(
    TALXIS.Platform.Metadata.Components.FormMetadata Form,
    string? UniqueName);

internal sealed record ViewInspectionResult(
    string? ViewId,
    string? DisplayName,
    string? EntityLogicalName,
    int? QueryType,
    bool IsDefault,
    bool IsQuickFindQuery,
    IReadOnlyList<ViewColumnNode> Columns,
    IReadOnlyList<string> OrderBy,
    string? FetchXml);

internal sealed record ViewColumnNode(
    string? Name,
    int? Width);

internal sealed record FormTabNode(
    string? Id,
    string? Name,
    string? Label,
    IReadOnlyList<FormSectionNode>? Sections);

internal sealed record FormSectionNode(
    string? Id,
    string? Name,
    string? Label,
    IReadOnlyList<FormControlNode>? Controls);

internal sealed record FormControlNode(
    string? Id,
    string? DataFieldName,
    string? ClassId,
    string? Label);

internal sealed record EntityInspectionResult(
    string? LogicalName,
    string? SchemaName,
    string? DisplayName,
    string? PrimaryIdAttribute,
    string? PrimaryNameAttribute,
    string Ownership,
    bool IsCustomEntity,
    IReadOnlyList<EntityAttributeNode> Attributes);

internal sealed record EntityAttributeNode(
    string? LogicalName,
    string Type,
    string? DisplayName,
    string RequiredLevel,
    bool IsRequired,
    bool IsCustomAttribute);
