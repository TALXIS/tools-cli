# Fast Component Comparison Design — Layer JSON vs Local Files

## 1. Executive Summary

The previous feasibility report recommended temp-export + SolutionPackager unpack. That approach is correct for **full fidelity** comparison of all 100+ component types, but too slow for the inner dev loop (10–30 seconds per export). This design targets **declarative components only** using `msdyn_componentlayers` API responses (~50ms per component) compared against local SolutionPackager-unpacked XML files.

**Key insight:** We do NOT need to bridge two arbitrary formats. The layer JSON is a flat key-value bag (serialized `Entity` record), and the local XML has a well-defined structure. For each component type, there is a **finite, known set of properties** that matter for comparison. We build per-type normalizers that extract these properties from both sides into a common `Dictionary<string, string>` property bag, then compare.

**Target components:** Entity definitions, Attributes, Forms (SystemForm), Views (SavedQuery), Ribbons (RibbonDiffXml), SiteMaps, App Modules, SCF components.

---

## 2. Architecture Overview

```
┌─────────────────────┐     ┌──────────────────────┐
│  Local XML Files    │     │  Dataverse Server     │
│  (SolutionPackager)  │     │  (msdyn_componentlayers) │
└─────────┬───────────┘     └──────────┬───────────┘
          │                            │
     LocalParser                  LayerParser
     (per-type)                   (per-type)
          │                            │
          ▼                            ▼
   ┌──────────────────────────────────────┐
   │     PropertyBag (common format)      │
   │   Dictionary<string, string>         │
   │   + ContentBlobs (formxml, fetchxml) │
   └──────────────┬───────────────────────┘
                  │
           ┌──────┴──────┐
           │             │
      Tier 1:       Tier 2:
      Hash Compare  Property Diff
      (SHA-256)     (key-by-key)
```

### Two-Tier Comparison

- **Tier 1 — Hash (instant):** Compute SHA-256 of the canonical property bag serialization. If hashes match → component unchanged. This covers 90%+ of components in a typical dev loop.
- **Tier 2 — Property diff (fast):** When hashes differ, report exactly which properties changed, with old/new values for metadata properties and structural diff for content blobs (formxml, fetchxml, layoutxml).

---

## 3. Layer JSON Structure (from live queries)

All component types return the same envelope:

```json
{
  "LogicalName": "<ComponentTypeName>",
  "Id": "<guid>",
  "Attributes": [
    { "Key": "<propertyname>", "Value": <value> }
  ]
}
```

### Concrete Examples (from `org2928f636.crm.dynamics.com`, solution "Model")

#### Entity (135 attributes)
Key properties map directly to metadata table columns:
```
entityid, name, logicalname, entitysetname, ownershiptypemask (int: 1=User),
isactivity (bool), isduplicatechecksupported (bool), isauditenabled (bool),
isconnectionsenabled (bool), isvisibleinmobile (bool), iscustomizable (bool/ManagedProperty),
changetrackingenabled (bool), synctoexternalsearchindex (bool), ismailmergeenabled (bool),
Description - LocalizedLabel (JSON), LocalizedName - LocalizedLabel (JSON)
```

#### SystemForm (26 attributes)
```
formid, name, type ({Value: 2}), objecttypecode, formxml (XML string!),
formjson (JSON string), isdefault, iscustomizable, description,
formactivationstate ({Value: 1}), formpresentation ({Value: 1}),
name - LocalizedLabel (JSON), description - LocalizedLabel (JSON)
```

#### SavedQuery/View (31 attributes)
```
savedqueryid, name, querytype, returnedtypecode, isdefault, isuserdefined,
fetchxml (XML string!), layoutxml (XML string!), layoutjson (JSON string),
iscustomizable, canbedeleted, isquickfindquery, offlinesqlquery,
name - LocalizedLabel (JSON)
```

---

## 4. Local XML Structure (SolutionPackager output)

### Entity.xml
```
Entities/<logicalname>/Entity.xml
  → <Entity> / <EntityInfo> / <entity Name="...">
    → <LocalizedNames>, <Descriptions>
    → <attributes> / <attribute> (child components)
    → <EntitySetName>, <IsDuplicateCheckSupported>, <OwnershipTypeMask>, ...
    → <RibbonDiffXml> (inline)
```

### Form XML
```
Entities/<logicalname>/FormXml/<type>/{formid}.xml
  → <forms> / <systemform>
    → <formid>, <IntroducedVersion>, <FormPresentation>, <FormActivationState>
    → <form> ... </form>  (the actual form definition XML)
```

### SavedQuery/View XML
```
Entities/<logicalname>/SavedQueries/{savedqueryid}.xml
  → <savedqueries> / <savedquery>
    → <savedqueryid>, <querytype>, <isdefault>, <IsCustomizable>
    → <fetchxml> ... </fetchxml>
    → <layoutxml> ... </layoutxml>
    → <LocalizedNames>
```

---

## 5. Per-Type Comparison Design

### 5.1 Entity Definition

**Server (layer JSON):** Flat key-value bag with 135 attributes. Keys use lowercase logical names from the metadata table (e.g., `entitysetname`, `isduplicatechecksupported`, `ownershiptypemask`).

**Local (Entity.xml):** Hierarchical XML under `<EntityInfo>/<entity>`. Properties are XML child elements with PascalCase names (e.g., `<EntitySetName>`, `<IsDuplicateCheckSupported>`, `<OwnershipTypeMask>`).

**Property mapping (from `ComponentPropertiesHelper.cs`):** The server's `PropMap` class defines the exact mapping: `PropMap("EntitySetName", "entitysetname", typeof(string))`. The first argument is the **XML element name**, the second is the **layer JSON key**. We replicate this mapping table.

**Normalization rules:**
| Layer JSON type | XML representation | Normalization |
|---|---|---|
| `bool` (`True`/`False`) | `1`/`0` | Normalize both to `true`/`false` |
| `int` (e.g., `ownershiptypemask: 1`) | Enum string (e.g., `UserOwned`) | Map enum strings to int values; compare as int |
| `ManagedProperty` (e.g., `{Value: True, CanBeChanged: True}`) | `1`/`0` | Extract `.Value`, normalize to bool |
| `LocalizedLabel` (JSON) | `<LocalizedNames>/<LocalizedName description="..." languagecode="1033" />` | Extract base LCID (1033) label text from both |
| `string` | Text content | Direct comparison |
| `OptionSetValue` (e.g., `{Value: 0}`) | String or int | Extract `.Value` |

**Content blobs:** `RibbonDiffXml` appears as an outer node in XML and as a key with XML string value in the layer JSON. Compare as normalized XML.

**What to compare:** Only the ~65 properties defined in `ComponentPropertiesHelper.cs` entity mapping. Ignore server-only metadata (e.g., `componentstate`, `solutionid`, `overwritetime`, `versionnumber`, `objecttypecode`, `physicalname`, `basetablename`).

**Comparison set (entity-level properties only, not sub-components):**
```csharp
// From ComponentPropertiesHelper.cs — "entity" PropMap list
static readonly Dictionary<string, string> EntityPropMap = new()
{
    // XML element name → layer JSON key
    ["EntitySetName"] = "entitysetname",
    ["IsDuplicateCheckSupported"] = "isduplicatechecksupported",
    ["IsBusinessProcessEnabled"] = "isbusinessprocessenabled",
    ["IsRequiredOffline"] = "isrequiredoffline",
    ["IsCollaboration"] = "iscollaboration",
    ["AutoRouteToOwnerQueue"] = "autoroutetoownerqueue",
    ["IsConnectionsEnabled"] = "isconnectionsenabled",
    ["EntityColor"] = "entitycolor",
    ["IsDocumentManagementEnabled"] = "isdocumentmanagementenabled",
    ["AutoCreateAccessTeams"] = "autocreateaccessteams",
    ["IsOneNoteIntegrationEnabled"] = "isonenoteintegrationenabled",
    ["IsKnowledgeManagementEnabled"] = "isknowledgemanagementenabled",
    ["IsSLAEnabled"] = "isslaenabled",
    ["OwnershipTypeMask"] = "ownershiptypemask",
    ["EntityMask"] = "entitymask",
    ["IsAuditEnabled"] = "isauditenabled",
    ["IsActivity"] = "isactivity",
    ["IsActivityParty"] = "isactivityparty",
    ["IsMailMergeEnabled"] = "ismailmergeenabled",
    ["IsVisibleInMobile"] = "isvisibleinmobile",
    ["IsVisibleInMobileClient"] = "isvisibleinmobileclient",
    ["IsReadOnlyInMobileClient"] = "isreadonlyinmobileclient",
    ["IsOfflineInMobileClient"] = "isofflineinmobileclient",
    ["IsQuickCreateEnabled"] = "isquickcreateenabled",
    ["SyncToExternalSearchIndex"] = "synctoexternalsearchindex",
    ["ChangeTrackingEnabled"] = "changetrackingenabled",
    ["IntroducedVersion"] = "introducedversion",
    ["IsCustomizable"] = "iscustomizable",        // ManagedProperty
    ["IsRenameable"] = "isrenameable",              // ManagedProperty
    ["EnforceStateTransitions"] = "enforcestatetransitions",
    // ... ~35 more from ComponentPropertiesHelper
};
```

### 5.2 Attribute

**Server (layer JSON):** Flat key-value bag (71 attributes). Layer per attribute, queried by attribute's `metadataId`.

**Local (Entity.xml):** Nested inside `<attributes>/<attribute>` within Entity.xml. Each attribute has XML child elements: `<Name>`, `<LogicalName>`, `<RequiredLevel>`, `<Type>`, etc. Also has type-specific sub-properties from type-specific PropMap lists (`int`, `nvarchar`, `decimal`, `money`, `lookup`, etc.).

**Property mapping (from `ComponentPropertiesHelper.cs`):**
```csharp
// "attribute" base props
["Name"] = "name",
["LogicalName"] = "logicalname",
["RequiredLevel"] = "attributerequiredlevelid",
["DisplayMask"] = "displaymask",           // int → flags enum
["ImeMode"] = "attributeimemodeid",
["ValidForUpdateApi"] = "validforupdateapi",
["IsCustomField"] = "iscustomfield",
["IsAuditEnabled"] = "isauditenabled",
["IsSecured"] = "issecured",
["IntroducedVersion"] = "introducedversion",
// ... plus type-specific props from "nvarchar", "int", "decimal", "lookup", etc.
```

**Challenge:** Attribute type determines which additional PropMap list applies (e.g., `nvarchar` adds `Format`, `MaxLength`, `Length`; `decimal` adds `MinValue`, `MaxValue`, `Accuracy`). The attribute's `Type` element in XML tells us which sub-map to use. The layer JSON key `attributetypeid` provides the type GUID which maps to the type name.

**Approach:** Parse the attribute XML's `<Type>` element to determine which PropMap list applies. Merge the base `attribute` props with the type-specific props. Extract matching keys from layer JSON.

### 5.3 SystemForm (Form)

**Server (layer JSON):** 26 attributes. The critical property is `formxml` — a string containing the actual form definition XML. Other metadata: `name`, `type`, `objecttypecode`, `formactivationstate`, `formpresentation`, `iscustomizable`.

**Local (FormXml/{type}/{formid}.xml):** Wrapper XML:
```xml
<forms>
  <systemform>
    <formid>{guid}</formid>
    <IntroducedVersion>1.0.0.0</IntroducedVersion>
    <FormPresentation>1</FormPresentation>
    <FormActivationState>1</FormActivationState>
    <form shownavigationbar="false" ...>
      <!-- actual form definition -->
    </form>
  </systemform>
</forms>
```

**Comparison strategy:**
1. **Metadata properties:** Compare `FormPresentation`, `FormActivationState`, `name`, `isdefault`, `iscustomizable` as simple properties.
2. **Form definition (content blob):** Extract `<form>` element from local XML and `formxml` string from layer JSON. Both contain the form definition XML. Normalize and compare as XML (canonical form — sorted attributes, normalized whitespace).

**Key insight:** The layer JSON `formxml` value is the **inner XML of `<form>`** — it's the `<form>...</form>` element as a string. The local file wraps it in `<forms>/<systemform>/<form>`. We extract the `<form>` element from the local file and compare against the layer's `formxml`.

### 5.4 SavedQuery (View)

**Server (layer JSON):** 31 attributes. Critical content properties: `fetchxml` (string), `layoutxml` (string). Metadata: `name`, `querytype`, `returnedtypecode`, `isdefault`, `iscustomizable`, `canbedeleted`.

**Local (SavedQueries/{savedqueryid}.xml):** Wrapper XML:
```xml
<savedqueries>
  <savedquery>
    <savedqueryid>{guid}</savedqueryid>
    <fetchxml>...</fetchxml>
    <layoutxml>...</layoutxml>
    <querytype>0</querytype>
    <IsCustomizable>1</IsCustomizable>
    ...
  </savedquery>
</savedqueries>
```

**Comparison strategy:**
1. **Metadata properties:** Direct mapping: `<querytype>` → `querytype`, `<isdefault>` → `isdefault`, `<IsCustomizable>` → `iscustomizable`, `<CanBeDeleted>` → `canbedeleted`, `<isquickfindquery>` → `isquickfindquery`.
2. **Content blobs:** `<fetchxml>` inner XML ↔ layer `fetchxml` string. `<layoutxml>` inner XML ↔ layer `layoutxml` string. Compare as normalized XML.

**Note:** The layer `fetchxml` includes a `savedqueryid` attribute in the `<fetch>` element that may not be present in the local file. Strip it before comparison.

### 5.5 RibbonDiffXml

**Server (layer JSON):** Part of the Entity layer JSON — appears as a key `RibbonDiffXml` (confirmed from `GraphComparer.cs` which has `_ribbonDiffNodeName = "RibbonDiffXml"`). The value is an XML string.

**Local:** Inside Entity.xml as `<RibbonDiffXml>` child element of the entity. SolutionPackager may also extract it to a separate file: `Entities/<logicalname>/RibbonDiff.xml` or `RibbonDiffXml/...`.

**Comparison:** Normalize both sides as XML and compare. The server's `EntityRootXMLConverterSubHandler` treats `RibbonDiffXml` as an "outer node" — it's serialized as an XML string in the graph node.

### 5.6 App Module

**Server (layer JSON):** App modules have their own component type. Layer JSON contains metadata like `uniquename`, `name`, `description`, `clienttype`, `url`, etc.

**Local:** SolutionPackager extracts to `AppModules/<uniquename>.xml`. Contains `<AppModule>` element with child properties.

**Comparison:** Similar pattern to Entity — metadata property mapping. The `ComponentPropertiesHelper` doesn't define AppModule props explicitly (they're resolved dynamically via entity metadata), but the key properties are: `uniquename`, `name`, `description`, `clienttype`, `url`, `appmoduleversion`, `iscustomizable`.

### 5.7 SiteMap

**Server (layer JSON):** `AppModuleSiteMap` component type. Contains `sitemapxml` property (XML string).

**Local:** `SiteMaps/<uniquename>.xml` containing the sitemap XML structure.

**Comparison:** Content blob comparison — extract `sitemapxml` from layer and compare with local file's inner XML. Normalize as XML.

### 5.8 SCF Components

**Server (layer JSON):** Component type varies (runtime-assigned). Layer JSON contains the component's metadata properties.

**Local:** Individual files or concatenated (depends on `filescope` from `solutioncomponentconfiguration`). May be XML, JSON, or YAML.

**Comparison:** SCF components are the most variable. Strategy:
1. Resolve type code via `solutioncomponentdefinitions` API (by name, not code).
2. Fetch layer JSON for the component.
3. Parse local file (XML/JSON/YAML → unified model).
4. Compare property-by-property using the export key attributes.

**Note:** SCF comparison is lower priority. The generic approach (compare all non-metadata keys) works for many SCF types. Type-specific normalizers can be added later.

---

## 6. Common Intermediate Format

```csharp
/// <summary>
/// Normalized property bag for a single component instance.
/// Produced by both LocalParser and LayerParser.
/// </summary>
public record ComponentPropertyBag
{
    /// <summary>Component type code (1=Entity, 2=Attribute, 26=SavedQuery, 60=SystemForm, etc.)</summary>
    public int ComponentType { get; init; }

    /// <summary>Component's primary key (entity name, form GUID, view GUID, etc.)</summary>
    public string PrimaryKey { get; init; }

    /// <summary>Comparable metadata properties. Keys are normalized canonical names (lowercase).</summary>
    public Dictionary<string, string> Properties { get; init; } = new();

    /// <summary>
    /// Content blobs — XML content properties that need structural comparison.
    /// Keys: "formxml", "fetchxml", "layoutxml", "ribbondiffxml", "sitemapxml"
    /// Values: Normalized XML strings (canonical form).
    /// </summary>
    public Dictionary<string, string> ContentBlobs { get; init; } = new();

    /// <summary>Localized labels by LCID → label text (for base language comparison).</summary>
    public Dictionary<string, string> Labels { get; init; } = new();

    /// <summary>SHA-256 hash of the canonical serialization (Properties + ContentBlobs + Labels, sorted).</summary>
    public string Hash => ComputeHash();

    private string ComputeHash()
    {
        // Deterministic serialization: sorted keys, normalized values
        var sb = new StringBuilder();
        foreach (var kvp in Properties.OrderBy(k => k.Key))
            sb.Append($"{kvp.Key}={kvp.Value}\n");
        foreach (var kvp in ContentBlobs.OrderBy(k => k.Key))
            sb.Append($"blob:{kvp.Key}={kvp.Value}\n");
        foreach (var kvp in Labels.OrderBy(k => k.Key))
            sb.Append($"label:{kvp.Key}={kvp.Value}\n");
        return SHA256Hash(sb.ToString());
    }
}
```

---

## 7. Normalization Rules

### 7.1 Value Normalization (Layer JSON → canonical string)

| JSON Value Type | Example | Canonical String |
|---|---|---|
| `bool` | `True`, `False` | `"true"`, `"false"` |
| `int` | `1` | `"1"` |
| `string` | `"fin_mytable"` | `"fin_mytable"` (trimmed) |
| `OptionSetValue` | `{"Value": 2}` | `"2"` |
| `ManagedProperty` | `{"Value": True, "CanBeChanged": True}` | `"true"` (extract `.Value` only) |
| `EntityReference` | `{"Id": "...", "LogicalName": "..."}` | Skip (not comparable) |
| `LocalizedLabel` | `{"LocalizedLabels": {"1033": {"LocalizedLabel": "My Table"}}}` | Extract LCID 1033 → `"My Table"` |
| `null` / missing | — | Skip key (absence = default) |

### 7.2 Value Normalization (Local XML → canonical string)

| XML Pattern | Example | Canonical String |
|---|---|---|
| Boolean element | `<IsAuditEnabled>1</IsAuditEnabled>` | `"true"` (1→true, 0→false) |
| Boolean element | `<IsAuditEnabled>0</IsAuditEnabled>` | `"false"` |
| Int element | `<OwnershipTypeMask>UserOwned</OwnershipTypeMask>` | `"1"` (enum name → int via lookup) |
| String element | `<EntitySetName>fin_mytables</EntitySetName>` | `"fin_mytables"` |
| ManagedProperty | `<IsCustomizable>1</IsCustomizable>` | `"true"` |
| LocalizedName | `<LocalizedName description="My Table" languagecode="1033" />` | Extract `@description` for LCID 1033 |

### 7.3 XML Content Normalization

For content blobs (`formxml`, `fetchxml`, `layoutxml`, `ribbondiffxml`):

```csharp
static string NormalizeXml(string xml)
{
    var doc = XDocument.Parse(xml);
    // 1. Sort attributes alphabetically on each element
    SortAttributes(doc.Root);
    // 2. Remove XML declaration, BOM
    // 3. Normalize whitespace (indent consistently)
    // 4. Strip known volatile attributes (e.g., savedqueryid in <fetch>)
    return doc.Root.ToString(SaveOptions.None);
}
```

**Volatile attributes to strip before comparison:**
- `<fetch>` → remove `savedqueryid` attribute (added during export, not in local files)
- `<grid>` → `object` attribute (objecttypecode, can differ)
- Timestamps, version numbers where they appear in embedded XML

---

## 8. Fetching Server State

### 8.1 Enumerate Components in Solution

```
GET msdyn_solutioncomponentsummaries
  ?$filter=msdyn_solutionid eq '{solutionId}'
  &$select=msdyn_displayname,msdyn_componenttype,msdyn_objectid,msdyn_componenttypename
```

This returns all root-level components. For sub-components (forms, views, attributes), we need to query them by parent entity:

- **Forms:** `GET systemforms?$filter=objecttypecode eq '{logicalname}'&$select=formid,type`
- **Views:** `GET savedqueries?$filter=returnedtypecode eq '{logicalname}'&$select=savedqueryid,querytype`
- **Attributes:** No separate query needed — we compare entity-level properties and use attribute layers only for attribute-level diff.

### 8.2 Fetch Layer JSON Per Component

```
GET msdyn_componentlayers
  ?$filter=msdyn_componentid eq '{objectId}' and msdyn_solutioncomponentname eq '{typeName}'
  &$select=msdyn_componentjson,msdyn_solutionname,msdyn_order
  &$orderby=msdyn_order desc
  &$top=1
```

The top layer (highest `msdyn_order`) gives the **effective active state**. This is what we compare against.

### 8.3 Bulk Optimization

For a solution with N components, we make:
- 1 query for solution component summary
- N queries for layer JSON (one per component)

With parallel requests (e.g., 10 concurrent), a 50-component solution takes ~500ms total. This is **orders of magnitude faster** than export+unpack.

**Further optimization:** We can batch queries using `$filter=msdyn_componentid eq 'id1' or msdyn_componentid eq 'id2' or ...` with the `msdyn_solutioncomponentname` grouped by type. OData `$filter` supports up to ~2000 chars.

---

## 9. Parsing Local State

### 9.1 File Discovery

Given a local solution path (e.g., `./src/Solutions.Model/Declarations/`):

```
Entities/{logicalname}/
  Entity.xml                          → Entity definition + attributes
  FormXml/{type}/{formid}.xml         → Forms (type: main, quick, card, etc.)
  SavedQueries/{savedqueryid}.xml     → Views
  RibbonDiff.xml                      → Ribbon customizations (optional separate file)
AppModules/{uniquename}.xml           → App modules
SiteMaps/{uniquename}.xml             → Sitemaps
```

### 9.2 Entity XML Parsing

```csharp
// Parse Entity.xml → extract entity-level properties only
var doc = XDocument.Load("Entity.xml");
var entityNode = doc.Root
    .Element("EntityInfo")
    .Element("entity");

var bag = new ComponentPropertyBag { ComponentType = 1 };

// Entity-level scalar properties
foreach (var (xmlName, jsonKey) in EntityPropMap)
{
    var elem = entityNode.Element(xmlName);
    if (elem != null)
        bag.Properties[jsonKey] = NormalizeXmlValue(elem, xmlName);
}

// Labels
var localizedNames = entityNode.Element("LocalizedNames");
if (localizedNames != null)
    bag.Labels["name"] = ExtractLabel(localizedNames, 1033);

// RibbonDiffXml (outer node)
var ribbon = doc.Root.Element("RibbonDiffXml");
if (ribbon != null)
    bag.ContentBlobs["ribbondiffxml"] = NormalizeXml(ribbon.ToString());
```

### 9.3 Form XML Parsing

```csharp
var doc = XDocument.Load("{formid}.xml");
var systemform = doc.Root.Element("systemform");

var bag = new ComponentPropertyBag { ComponentType = 60 };
bag.PrimaryKey = systemform.Element("formid").Value.Trim('{', '}');
bag.Properties["formpresentation"] = systemform.Element("FormPresentation")?.Value;
bag.Properties["formactivationstate"] = systemform.Element("FormActivationState")?.Value;

// The <form> element is the content blob
var formElem = systemform.Element("form");
if (formElem != null)
    bag.ContentBlobs["formxml"] = NormalizeXml(formElem.ToString());
```

### 9.4 View XML Parsing

```csharp
var doc = XDocument.Load("{savedqueryid}.xml");
var savedquery = doc.Root.Element("savedquery");

var bag = new ComponentPropertyBag { ComponentType = 26 };
bag.PrimaryKey = savedquery.Element("savedqueryid").Value.Trim('{', '}');
bag.Properties["querytype"] = savedquery.Element("querytype")?.Value;
bag.Properties["isdefault"] = NormalizeBool(savedquery.Element("isdefault")?.Value);
bag.Properties["iscustomizable"] = NormalizeBool(savedquery.Element("IsCustomizable")?.Value);

// Content blobs
var fetchxml = savedquery.Element("fetchxml");
if (fetchxml != null)
    bag.ContentBlobs["fetchxml"] = NormalizeXml(fetchxml.FirstNode.ToString());

var layoutxml = savedquery.Element("layoutxml");
if (layoutxml != null)
    bag.ContentBlobs["layoutxml"] = NormalizeXml(layoutxml.FirstNode.ToString());
```

---

## 10. Comparison Algorithm

```csharp
public record ComparisonResult
{
    public string ComponentKey { get; init; }
    public int ComponentType { get; init; }
    public ChangeType Change { get; init; }  // Unchanged, Modified, LocalOnly, ServerOnly
    public List<PropertyDiff> Diffs { get; init; } = new();
}

public record PropertyDiff
{
    public string PropertyName { get; init; }
    public string LocalValue { get; init; }
    public string ServerValue { get; init; }
    public DiffKind Kind { get; init; }  // Metadata, ContentBlob, Label
}

public enum ChangeType { Unchanged, Modified, LocalOnly, ServerOnly }

public static ComparisonResult Compare(
    ComponentPropertyBag local,
    ComponentPropertyBag server)
{
    // Tier 1: Hash check
    if (local.Hash == server.Hash)
        return new ComparisonResult { Change = ChangeType.Unchanged };

    // Tier 2: Property-level diff
    var diffs = new List<PropertyDiff>();

    // Compare metadata properties
    var allKeys = local.Properties.Keys.Union(server.Properties.Keys);
    foreach (var key in allKeys)
    {
        var localVal = local.Properties.GetValueOrDefault(key);
        var serverVal = server.Properties.GetValueOrDefault(key);
        if (localVal != serverVal)
            diffs.Add(new PropertyDiff
            {
                PropertyName = key,
                LocalValue = localVal,
                ServerValue = serverVal,
                Kind = DiffKind.Metadata
            });
    }

    // Compare content blobs
    var allBlobKeys = local.ContentBlobs.Keys.Union(server.ContentBlobs.Keys);
    foreach (var key in allBlobKeys)
    {
        var localBlob = local.ContentBlobs.GetValueOrDefault(key);
        var serverBlob = server.ContentBlobs.GetValueOrDefault(key);
        if (localBlob != serverBlob)
            diffs.Add(new PropertyDiff
            {
                PropertyName = key,
                LocalValue = localBlob?.Substring(0, Math.Min(100, localBlob.Length)),
                ServerValue = serverBlob?.Substring(0, Math.Min(100, serverBlob.Length)),
                Kind = DiffKind.ContentBlob
            });
    }

    // Compare labels
    var allLabelKeys = local.Labels.Keys.Union(server.Labels.Keys);
    foreach (var key in allLabelKeys)
    {
        var localLabel = local.Labels.GetValueOrDefault(key);
        var serverLabel = server.Labels.GetValueOrDefault(key);
        if (localLabel != serverLabel)
            diffs.Add(new PropertyDiff
            {
                PropertyName = $"label:{key}",
                LocalValue = localLabel,
                ServerValue = serverLabel,
                Kind = DiffKind.Label
            });
    }

    return new ComparisonResult
    {
        Change = diffs.Any() ? ChangeType.Modified : ChangeType.Unchanged,
        Diffs = diffs
    };
}
```

---

## 11. Enum Mapping Tables

The local XML uses enum string names (e.g., `UserOwned`) while the layer JSON uses integer values (e.g., `1`). We need mapping tables for:

```csharp
static readonly Dictionary<string, Dictionary<string, int>> EnumMaps = new()
{
    ["OwnershipTypes"] = new()
    {
        ["None"] = 0, ["UserOwned"] = 1, ["TeamOwned"] = 2, ["BusinessOwned"] = 4,
        ["OrganizationOwned"] = 8, ["BusinessParented"] = 16
    },
    ["EntityMasks"] = new()
    {
        // Flags enum — the XML may show combined values
        ["ValidForAdvancedFind"] = 0x001, ["ValidForForm"] = 0x002,
        ["ValidForGrid"] = 0x004, /* ... */
    },
    ["ActivityTypeMasks"] = new() { /* ... */ },
    ["CascadeLinkType"] = new()
    {
        ["NoCascade"] = 0, ["Cascade"] = 1, ["Active"] = 2, ["UserOwned"] = 3,
        ["RemoveLink"] = 4, ["Restrict"] = 5
    },
    // DisplayMask is a special case — pipe-separated flags in XML
};
```

The `DisplayMask` field is particularly tricky: XML has `ValidForAdvancedFind|ValidForForm|ValidForGrid` while the JSON has an integer. We need to parse the pipe-separated flags and OR them together.

---

## 12. Shared Logic Across Types

### 12.1 Shared Infrastructure

| Component | Shared? | Notes |
|---|---|---|
| `LayerJsonParser` | ✅ Fully shared | Same envelope for all types. Extract `Attributes` array → `Dictionary<string, object>` |
| `ValueNormalizer` | ✅ Fully shared | `bool`, `int`, `string`, `OptionSetValue`, `ManagedProperty`, `EntityReference`, `LocalizedLabel` |
| `XmlContentNormalizer` | ✅ Fully shared | Canonical XML for content blobs |
| `PropertyBagHasher` | ✅ Fully shared | SHA-256 of sorted property bag |
| `PropertyBagComparer` | ✅ Fully shared | Tier 2 key-by-key diff |
| `EnumMapper` | ✅ Shared tables | Lookup tables for all enum types |

### 12.2 Per-Type Components

| Component | Type-specific | Effort |
|---|---|---|
| `EntityLocalParser` | Entity.xml XPath extraction | 2 days |
| `EntityLayerMapper` | PropMap filtering (65 keys from 135) | 1 day |
| `FormLocalParser` | FormXml file discovery + parsing | 1 day |
| `FormLayerMapper` | `formxml` extraction | 0.5 day |
| `ViewLocalParser` | SavedQuery file parsing | 0.5 day |
| `ViewLayerMapper` | `fetchxml`/`layoutxml` extraction | 0.5 day |
| `AttributeLocalParser` | Attribute parsing from Entity.xml | 1.5 days |
| `AttributeLayerMapper` | Type-specific PropMap selection | 1 day |
| `RibbonLocalParser` | RibbonDiffXml extraction | 0.5 day |
| `AppModuleLocalParser` | AppModule file parsing | 0.5 day |
| `SiteMapLocalParser` | SiteMap file parsing | 0.5 day |
| `ScfLocalParser` | Generic SCF file parsing | 1 day |

---

## 13. Effort Estimates

### Phase 1: Core Infrastructure (5 days)

| Task | Effort | Priority |
|---|---|---|
| `LayerJsonParser` (parse msdyn_componentjson envelope) | 0.5 day | P0 |
| `ValueNormalizer` (bool, int, string, OptionSetValue, ManagedProperty, LocalizedLabel) | 1 day | P0 |
| `XmlContentNormalizer` (canonical XML for content blobs) | 1 day | P0 |
| `ComponentPropertyBag` data structure + hash | 0.5 day | P0 |
| `PropertyBagComparer` (Tier 1 hash + Tier 2 diff) | 1 day | P0 |
| `EnumMapper` tables (OwnershipTypes, DisplayMask, CascadeLinkType, etc.) | 1 day | P0 |

### Phase 2: Per-Type Parsers (6 days)

| Task | Effort | Priority |
|---|---|---|
| Entity definition (local + layer, ~65 properties) | 2 days | P0 |
| SystemForm (local + layer, content blob: formxml) | 1 day | P0 |
| SavedQuery/View (local + layer, content blobs: fetchxml, layoutxml) | 1 day | P0 |
| Attribute (local + layer, type-specific PropMaps) | 1.5 days | P1 |
| RibbonDiffXml (content blob comparison) | 0.5 day | P1 |

### Phase 3: Extended Types (3 days)

| Task | Effort | Priority |
|---|---|---|
| App Module parser | 0.5 day | P2 |
| SiteMap parser | 0.5 day | P2 |
| SCF generic parser | 1 day | P2 |
| Integration + CLI command (`txc sln compare`) | 1 day | P1 |

### Phase 4: Testing & Edge Cases (3 days)

| Task | Effort | Priority |
|---|---|---|
| Unit tests for normalizers | 1 day | P0 |
| Integration tests against live environment | 1 day | P0 |
| Edge cases (empty values, missing properties, managed vs unmanaged) | 1 day | P1 |

### Total: ~17 days (~3.5 weeks)

---

## 14. Risks & Mitigations

### Risk 1: Layer JSON format changes across Dataverse versions
**Mitigation:** The `msdyn_componentlayers` API has been stable since its introduction. The JSON envelope format (Attributes array of Key/Value pairs) is the standard Entity serialization format and unlikely to change.

### Risk 2: Enum value mapping is incomplete
**Mitigation:** We can fall back to string comparison when an enum is unknown. The most important enums (OwnershipTypes, CascadeLinkType, DisplayMask) are well-documented.

### Risk 3: XML normalization produces false positives/negatives
**Mitigation:** Use the same XML normalization approach as the server (`XElement.Parse().ToString()` — the server's `SourceControlHelper.SerializeFile` pattern). Strip known volatile attributes before comparison.

### Risk 4: Content blob XML differences are cosmetic (whitespace, attribute order)
**Mitigation:** The `NormalizeXml` function sorts attributes and normalizes whitespace. If issues persist, add a "deep XML comparison" mode using XNode.DeepEquals.

### Risk 5: Missing properties in local file vs abundant properties in layer JSON
**Mitigation:** Only compare the intersection of properties defined in `ComponentPropertiesHelper.cs`. Ignore properties present in one side but not the other unless they're in the comparison set.

---

## 15. Comparison with Export+Unpack Approach

| Criterion | Layer JSON Approach | Export+Unpack Approach |
|---|---|---|
| **Speed** | ~50ms per component, ~500ms for 50 components | 10–30 seconds per solution |
| **Coverage** | Declarative components only (Entity, Form, View, Attribute, Ribbon, SiteMap, AppModule, SCF) | All 100+ component types including binaries |
| **Fidelity** | Property-level (may miss edge cases) | Bit-perfect (canonical form match) |
| **Implementation effort** | ~3.5 weeks | ~2.5 weeks (simpler, no per-type code) |
| **Dependencies** | None (uses existing `msdyn_componentlayers` API) | SolutionPackager integration (Phase 0-1 prerequisite) |
| **Use case** | Inner dev loop: "did my form change?" | Pre-commit/CI: "exactly what changed?" |

**Recommendation:** Implement **both** approaches:
1. **Layer JSON (this design)** — for the inner dev loop. Fast, opinionated, declarative-only.
2. **Export+Unpack (previous design)** — for CI/CD and full-fidelity comparison. Slower but complete.

The layer JSON approach can serve as a "pre-filter" for the export approach: if hash says unchanged → skip the component. If hash says changed → export only that component for detailed comparison.

---

## 16. Implementation Order

1. **Start with Forms and Views** — highest developer ROI. These are what developers edit most frequently, and the comparison is straightforward (content blob + few metadata props).
2. **Add Entity definition** — most complex due to 65+ properties, but the mapping is fully defined by `ComponentPropertiesHelper.cs`.
3. **Add Attributes** — builds on Entity infrastructure, adds type-specific PropMaps.
4. **Add Ribbon, SiteMap, AppModule** — incremental additions using shared infrastructure.
5. **Add SCF** — last, most variable, needs type code resolution.
