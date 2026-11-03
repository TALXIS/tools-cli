using System.Xml;
using System.Xml.Linq;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Features.Environment.Connector.Operation;

/// <summary>One column of a table defined in the workspace, as its <c>Entity.xml</c> declares it.</summary>
internal sealed record WorkspaceColumn(
    string LogicalName,
    string Type,
    string? DisplayName,
    string? RequiredLevel,
    bool ValidForCreate,
    bool ValidForUpdate,
    bool ValidForRead,
    IReadOnlyList<(string Value, string? Label)>? Options,
    string? Target);

/// <summary>A table defined in the workspace: its sets of names and its columns from every solution that declares them.</summary>
internal sealed record WorkspaceTable(
    string LogicalName,
    string? EntitySetName,
    string? CollectionName,
    IReadOnlyList<WorkspaceColumn> Columns,
    IReadOnlyList<string> Files);

/// <summary>
/// Reads Dataverse tables from the workspace's solution sources
/// (<c>Entities/&lt;table&gt;/Entity.xml</c>, <c>Other/Relationships/*.xml</c>,
/// <c>OptionSets/*.xml</c>), with no environment involved. A table split
/// across solutions (columns in one, forms in another) is merged.
/// </summary>
internal static class WorkspaceTableReader
{
    /// <summary>The table whose entity set name or logical name is <paramref name="name"/>; null when the workspace does not define it.</summary>
    public static WorkspaceTable? Find(string workspaceRoot, string name)
    {
        var files = WorkspaceFiles.Enumerate(workspaceRoot, "Entity.xml")
            .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))), "Entities", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: f, Entity: Load(f)?.Root?.Element("EntityInfo")?.Element("entity")))
            .Where(e => e.Entity is not null)
            .ToList();

        var logicalName = files
            .Select(e => (Name: (string?)e.Entity!.Attribute("Name"), Set: e.Entity!.Element("EntitySetName")?.Value))
            .FirstOrDefault(e => string.Equals(e.Set, name, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            .Name;
        if (logicalName is null)
            return null;

        // The solution that declares the columns comes first: it is the file an answer cites.
        var declarations = files
            .Where(e => string.Equals((string?)e.Entity!.Attribute("Name"), logicalName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Entity!.Element("attributes")?.Elements("attribute").Count() ?? 0)
            .ToList();
        var targets = ReadLookupTargets(workspaceRoot, logicalName);
        var globalOptions = new Lazy<Dictionary<string, IReadOnlyList<(string, string?)>>>(() => ReadGlobalOptionSets(workspaceRoot));

        var columns = new Dictionary<string, WorkspaceColumn>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, entity) in declarations)
        {
            foreach (var attribute in entity!.Element("attributes")?.Elements("attribute") ?? [])
            {
                var column = ReadColumn(attribute, targets, globalOptions);
                if (column is not null)
                    columns.TryAdd(column.LogicalName, column);
            }
        }

        var first = declarations.Select(d => d.Entity!).FirstOrDefault(e => e.Element("EntitySetName") is not null) ?? declarations[0].Entity!;
        return new WorkspaceTable(
            logicalName,
            first.Element("EntitySetName")?.Value,
            Label(first.Element("LocalizedCollectionNames"), "LocalizedCollectionName"),
            columns.Values.ToList(),
            declarations.Select(d => d.Path).ToList());
    }

    /// <summary>The collection display name of a table the workspace defines, e.g. <c>Warehouse Locations</c>.</summary>
    public static string? CollectionName(string workspaceRoot, string logicalName)
        => Find(workspaceRoot, logicalName)?.CollectionName;

    private static WorkspaceColumn? ReadColumn(
        XElement attribute,
        IReadOnlyDictionary<string, string> targets,
        Lazy<Dictionary<string, IReadOnlyList<(string, string?)>>> globalOptions)
    {
        var logicalName = attribute.Element("LogicalName")?.Value;
        var type = attribute.Element("Type")?.Value;
        if (string.IsNullOrEmpty(logicalName) || string.IsNullOrEmpty(type))
            return null;

        IReadOnlyList<(string, string?)>? options = null;
        if (attribute.Element("optionset") is { } inline)
            options = ReadOptions(inline);
        else if (attribute.Element("OptionSetName")?.Value is { Length: > 0 } global && globalOptions.Value.TryGetValue(global, out var shared))
            options = shared;

        return new WorkspaceColumn(
            logicalName.ToLowerInvariant(),
            type.ToLowerInvariant(),
            Label(attribute.Element("displaynames"), "displayname"),
            attribute.Element("RequiredLevel")?.Value,
            Flag(attribute, "ValidForCreateApi"),
            Flag(attribute, "ValidForUpdateApi"),
            Flag(attribute, "ValidForReadApi"),
            options,
            targets.GetValueOrDefault(logicalName));
    }

    /// <summary>Picklist options or state/status values with their labels.</summary>
    private static IReadOnlyList<(string, string?)>? ReadOptions(XElement optionSet)
    {
        var values = (optionSet.Element("options")?.Elements("option")
                ?? optionSet.Element("states")?.Elements("state")
                ?? optionSet.Element("statuses")?.Elements("status")
                ?? [])
            .Select(o => ((string?)o.Attribute("value"), Label(o.Element("labels"), "label")))
            .Where(o => o.Item1 is not null)
            .Select(o => (o.Item1!, o.Item2))
            .ToList();
        return values.Count == 0 ? null : values;
    }

    /// <summary>The table each lookup column of <paramref name="table"/> points at, from the workspace's relationships.</summary>
    private static Dictionary<string, string> ReadLookupTargets(string workspaceRoot, string table)
    {
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in WorkspaceFiles.Enumerate(workspaceRoot, "*.xml").Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "Relationships"))
        {
            foreach (var relationship in Load(file)?.Descendants("EntityRelationship") ?? [])
            {
                if (string.Equals(relationship.Element("ReferencingEntityName")?.Value, table, StringComparison.OrdinalIgnoreCase)
                    && relationship.Element("ReferencingAttributeName")?.Value is { Length: > 0 } column
                    && relationship.Element("ReferencedEntityName")?.Value is { Length: > 0 } target)
                {
                    targets.TryAdd(column, target.ToLowerInvariant());
                }
            }
        }

        return targets;
    }

    private static Dictionary<string, IReadOnlyList<(string, string?)>> ReadGlobalOptionSets(string workspaceRoot)
    {
        var sets = new Dictionary<string, IReadOnlyList<(string, string?)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in WorkspaceFiles.Enumerate(workspaceRoot, "*.xml").Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "OptionSets"))
        {
            if (Load(file)?.Root is { } root && (string?)root.Attribute("Name") is { } name && ReadOptions(root) is { } options)
                sets.TryAdd(name, options);
        }

        return sets;
    }

    /// <summary>The English label (language 1033), else the first one.</summary>
    private static string? Label(XElement? container, string elementName)
    {
        var labels = container?.Elements(elementName).ToList() ?? [];
        var chosen = labels.FirstOrDefault(l => (string?)l.Attribute("languagecode") == "1033") ?? labels.FirstOrDefault();
        return chosen?.Attribute("description")?.Value is { Length: > 0 } text ? text : null;
    }

    private static bool Flag(XElement attribute, string name) => attribute.Element(name)?.Value == "1";

    private static XDocument? Load(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch (XmlException)
        {
            // Not a solution source txc can read; the build reports malformed XML.
            return null;
        }
    }
}
