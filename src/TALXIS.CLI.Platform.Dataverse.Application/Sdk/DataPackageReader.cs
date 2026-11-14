using System.IO.Compression;
using System.Xml.Linq;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Parsed contents of a CMT data package — just enough to drive cleanup.
/// We deliberately do not reuse the legacy CMT parser here because (a) it
/// runs in a subprocess and (b) it loads the whole record set as
/// <c>EntityCollection</c>s, which is more work than we need.
/// </summary>
internal sealed record DataPackageContents(
    IReadOnlyList<string> EntityImportOrder,
    IReadOnlyDictionary<string, DataPackageEntitySchema> Schemas,
    IReadOnlyDictionary<string, IReadOnlyList<DataPackageRecordRow>> Records,
    IReadOnlyList<DataPackageM2mAssociation> M2mAssociations);

/// <summary>
/// Schema slice for a single entity: the primary-id column plus the
/// natural-key fields used as the dedup fallback during cleanup.
/// </summary>
internal sealed record DataPackageEntitySchema(
    string LogicalName,
    string PrimaryIdField,
    string? PrimaryNameField,
    IReadOnlyList<string> NaturalKeyFields);

/// <summary>
/// Single record as it appears in <c>data.xml</c>.
/// </summary>
internal sealed record DataPackageRecordRow(
    Guid Id,
    IReadOnlyDictionary<string, string?> Fields);

/// <summary>
/// One source/target pair drawn from a <c>&lt;m2mrelationship&gt;</c> block.
/// One row per target id (denormalised so callers can issue
/// <c>DisassociateRequest</c> per pair).
/// </summary>
internal sealed record DataPackageM2mAssociation(
    string RelationshipName,
    string SourceEntity,
    Guid SourceId,
    string TargetEntity,
    Guid TargetId);

/// <summary>
/// Loads a CMT package from a folder or a <c>.zip</c> archive into an
/// in-memory <see cref="DataPackageContents"/>. Pure parsing — no IO beyond
/// reading the two XML files.
/// </summary>
internal static class DataPackageReader
{
    private const string DataFileName = "data.xml";
    private const string SchemaFileName = "data_schema.xml";

    public static DataPackageContents Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Data package path is required.", nameof(path));

        XDocument schemaDoc;
        XDocument dataDoc;

        if (Directory.Exists(path))
        {
            var schemaFile = Path.Combine(path, SchemaFileName);
            var dataFile = Path.Combine(path, DataFileName);
            if (!File.Exists(schemaFile) || !File.Exists(dataFile))
            {
                throw new InvalidOperationException(
                    $"Folder '{path}' does not contain required CMT files ({DataFileName} and {SchemaFileName}).");
            }
            schemaDoc = XDocument.Load(schemaFile);
            dataDoc = XDocument.Load(dataFile);
        }
        else if (File.Exists(path))
        {
            using var zip = ZipFile.OpenRead(path);
            schemaDoc = LoadEntryAsXml(zip, SchemaFileName, path);
            dataDoc = LoadEntryAsXml(zip, DataFileName, path);
        }
        else
        {
            throw new InvalidOperationException($"Data package not found: '{path}'.");
        }

        var importOrder = ParseEntityImportOrder(schemaDoc);
        var schemas = ParseSchemas(schemaDoc);
        var m2mDefs = ParseM2mRelationshipDefinitions(schemaDoc);
        var records = ParseRecords(dataDoc);
        var m2mAssociations = ParseM2mAssociations(dataDoc, m2mDefs);

        return new DataPackageContents(importOrder, schemas, records, m2mAssociations);
    }

    private static XDocument LoadEntryAsXml(ZipArchive zip, string name, string archivePath)
    {
        var entry = FindEntry(zip, name)
            ?? throw new InvalidOperationException(
                $"Archive '{archivePath}' does not contain required CMT file '{name}'.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string name)
    {
        foreach (var entry in zip.Entries)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    private static IReadOnlyList<string> ParseEntityImportOrder(XDocument schema)
    {
        var root = schema.Root;
        if (root is null) return Array.Empty<string>();

        var orderElement = root.Element("entityImportOrder");
        if (orderElement is null) return Array.Empty<string>();

        return orderElement.Elements("entityName")
            .Select(e => e.Value?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();
    }

    private static IReadOnlyDictionary<string, DataPackageEntitySchema> ParseSchemas(XDocument schema)
    {
        var dict = new Dictionary<string, DataPackageEntitySchema>(StringComparer.OrdinalIgnoreCase);
        var root = schema.Root;
        if (root is null) return dict;

        foreach (var entity in root.Elements("entity"))
        {
            var name = (string?)entity.Attribute("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var primaryId = (string?)entity.Attribute("primaryidfield") ?? string.Empty;
            var primaryName = (string?)entity.Attribute("primarynamefield");

            var naturalKey = new List<string>();
            if (!string.IsNullOrWhiteSpace(primaryName))
                naturalKey.Add(primaryName!);

            var fieldsRoot = entity.Element("fields");
            if (fieldsRoot is not null)
            {
                foreach (var field in fieldsRoot.Elements("field"))
                {
                    var fname = (string?)field.Attribute("name");
                    if (string.IsNullOrWhiteSpace(fname)) continue;

                    var update = ParseBool((string?)field.Attribute("updateCompare"));
                    if (update && !naturalKey.Contains(fname!, StringComparer.OrdinalIgnoreCase))
                        naturalKey.Add(fname!);
                }
            }

            dict[name!] = new DataPackageEntitySchema(name!, primaryId, primaryName, naturalKey);
        }

        return dict;
    }

    private static IReadOnlyDictionary<string, M2mRelationshipDef> ParseM2mRelationshipDefinitions(XDocument schema)
    {
        var dict = new Dictionary<string, M2mRelationshipDef>(StringComparer.OrdinalIgnoreCase);
        var root = schema.Root;
        if (root is null) return dict;

        foreach (var entity in root.Elements("entity"))
        {
            var sourceEntity = (string?)entity.Attribute("name");
            if (string.IsNullOrWhiteSpace(sourceEntity)) continue;

            var relationships = entity.Element("relationships");
            if (relationships is null) continue;

            foreach (var rel in relationships.Elements("relationship"))
            {
                var manyToMany = ParseBool((string?)rel.Attribute("manyToMany"));
                if (!manyToMany) continue;

                var relName = (string?)rel.Attribute("name");
                if (string.IsNullOrWhiteSpace(relName)) continue;

                var target = (string?)rel.Attribute("m2mTargetEntity");
                if (string.IsNullOrWhiteSpace(target)) continue;

                // Keep the first occurrence — both endpoints often declare the
                // same relationship, but the source-side declaration is the one
                // whose <m2mrelationships> block in data.xml lists the targets.
                dict.TryAdd(relName!, new M2mRelationshipDef(relName!, sourceEntity!, target!));
            }
        }

        return dict;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DataPackageRecordRow>> ParseRecords(XDocument data)
    {
        var dict = new Dictionary<string, IReadOnlyList<DataPackageRecordRow>>(StringComparer.OrdinalIgnoreCase);
        var root = data.Root;
        if (root is null) return dict;

        foreach (var entity in root.Elements("entity"))
        {
            var name = (string?)entity.Attribute("name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var records = new List<DataPackageRecordRow>();
            var recordsRoot = entity.Element("records");
            if (recordsRoot is not null)
            {
                foreach (var record in recordsRoot.Elements("record"))
                {
                    var idAttr = (string?)record.Attribute("id");
                    if (!Guid.TryParse(idAttr, out var id)) continue;

                    var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var field in record.Elements("field"))
                    {
                        var fname = (string?)field.Attribute("name");
                        if (string.IsNullOrWhiteSpace(fname)) continue;
                        var fvalue = (string?)field.Attribute("value");
                        fields[fname!] = fvalue;
                    }

                    records.Add(new DataPackageRecordRow(id, fields));
                }
            }

            dict[name!] = records;
        }

        return dict;
    }

    private static IReadOnlyList<DataPackageM2mAssociation> ParseM2mAssociations(
        XDocument data, IReadOnlyDictionary<string, M2mRelationshipDef> m2mDefs)
    {
        var list = new List<DataPackageM2mAssociation>();
        var root = data.Root;
        if (root is null) return list;

        foreach (var entity in root.Elements("entity"))
        {
            var sourceEntity = (string?)entity.Attribute("name");
            if (string.IsNullOrWhiteSpace(sourceEntity)) continue;

            var m2mRoot = entity.Element("m2mrelationships");
            if (m2mRoot is null) continue;

            foreach (var m2m in m2mRoot.Elements("m2mrelationship"))
            {
                var relName = (string?)m2m.Attribute("m2mrelationshipname");
                var sourceIdAttr = (string?)m2m.Attribute("sourceid");
                if (string.IsNullOrWhiteSpace(relName)) continue;
                if (!Guid.TryParse(sourceIdAttr, out var sourceId)) continue;

                // The relationship's target entity is recorded only in the schema.
                // Without a definition we can't issue a disassociate — skip silently;
                // the legacy CMT importer behaves the same way.
                if (!m2mDefs.TryGetValue(relName!, out var def)) continue;

                // The source endpoint in the schema may not match the entity that
                // is hosting the data block (e.g. reflexive N:N). Prefer the
                // hosting entity as the source so DisassociateRequest gets the
                // correct EntityReference type.
                var resolvedSource = string.Equals(def.SourceEntity, sourceEntity, StringComparison.OrdinalIgnoreCase)
                    ? def.SourceEntity
                    : sourceEntity!;
                var resolvedTarget = string.Equals(def.SourceEntity, sourceEntity, StringComparison.OrdinalIgnoreCase)
                    ? def.TargetEntity
                    : def.SourceEntity;

                var targetsRoot = m2m.Element("targetids");
                if (targetsRoot is null) continue;

                foreach (var t in targetsRoot.Elements("targetid"))
                {
                    if (!Guid.TryParse(t.Value, out var targetId)) continue;
                    list.Add(new DataPackageM2mAssociation(
                        relName!, resolvedSource, sourceId, resolvedTarget, targetId));
                }
            }
        }

        return list;
    }

    private static bool ParseBool(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || value == "1");

    private sealed record M2mRelationshipDef(string Name, string SourceEntity, string TargetEntity);
}
