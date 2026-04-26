# Solution Comparison Feasibility Report

## Objective

Assess the difficulty of building a `txc` command that compares unpacked solution files on local disk with component definitions in a live Dataverse environment. Tested against `https://org2928f636.crm.dynamics.com` with solutions "Model" (1 entity) and "Basic" (37 entities + 1 optionset).

---

## 1. Format Analysis: Server-Side `msdyn_componentjson`

The `msdyn_componentlayers` API returns per-layer component state. The key field is `msdyn_componentjson`, which is a **JSON string** (not XML) for all component types. Its structure is consistent:

```json
{
  "LogicalName": "<ComponentTypeName>",
  "Id": "<guid>",
  "Attributes": [
    { "Key": "<propertyname>", "Value": <value> },
    ...
  ]
}
```

This is essentially a serialized `Entity` record (Dataverse SDK `Entity` type) — a flat key-value bag.

There is also `msdyn_changes` which contains only the delta attributes introduced by that specific layer (vs. `msdyn_componentjson` which is the full merged state). For comparison purposes, `msdyn_componentjson` of the **top layer** (highest `msdyn_order`) gives the current effective state.

### Tested Component Types

| Component Type | `msdyn_solutioncomponentname` | `msdyn_componentjson` keys | Notable fields |
|---|---|---|---|
| **Entity (1)** | `Entity` | 135 keys | `name`, `logicalname`, boolean flags, `Description - LocalizedLabel`, `LocalizedName - LocalizedLabel` |
| **Attribute (2)** | `Attribute` | 71 keys | `name`, `logicalname`, `attributetypeid`, `length`, `isnullable`, etc. |
| **SystemForm (60)** | `SystemForm` | 26 keys | **`formxml`** (XML string), `formjson` (JSON string), `name`, `type`, `objecttypecode` |
| **SavedQuery/View (26)** | `SavedQuery` | 31 keys | **`fetchxml`** (XML string), **`layoutxml`** (XML string), `layoutjson`, `querytype`, `name` |
| **OptionSet (9)** | `OptionSet` | ~20 keys | `name`, `optionsettype`, `isglobal`, `iscustomoptionset`, option values |
| **SCF: KeyVaultReference (10031)** | `KeyVaultReference` | ~15 keys | `keyvaulturl`, `keytype`, standard metadata fields |

**Key finding:** The server JSON is a **flat key-value bag** of metadata properties. For components that contain "content" (forms, views), the actual XML payload is embedded as a string value within the JSON.

---

## 2. Format Analysis: Local Unpacked Files (SolutionPackager)

SolutionPackager produces a well-defined folder structure. Examined from `temp/deploy-e2e-txctest-scaffold/src/Solutions.Txctest/Declarations/`:

### Entity.xml

```xml
<Entity xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Name LocalizedName="ScaffoldItem" OriginalName="ScaffoldItem">txctest_scaffolditem</Name>
  <EntityInfo>
    <entity Name="txctest_scaffolditem">
      <LocalizedNames>...</LocalizedNames>
      <attributes>
        <attribute PhysicalName="CreatedBy">
          <Type>lookup</Type>
          <Name>createdby</Name>
          <LogicalName>createdby</LogicalName>
          <RequiredLevel>none</RequiredLevel>
          ...40+ properties per attribute...
        </attribute>
      </attributes>
      <EntitySetName>...</EntitySetName>
      ...entity-level flags...
    </entity>
  </EntityInfo>
</Entity>
```

### Form XML (e.g., `FormXml/main/{guid}.xml`)

```xml
<forms xmlns:xsi="...">
  <systemform>
    <formid>{guid}</formid>
    <IntroducedVersion>1.0.0.0</IntroducedVersion>
    <form shownavigationbar="false" ...>
      <tabs>...</tabs>
    </form>
  </systemform>
</forms>
```

### View XML (e.g., `SavedQueries/{guid}.xml`)

```xml
<savedqueries xmlns:xsi="...">
  <savedquery>
    <savedqueryid>{guid}</savedqueryid>
    <fetchxml>...</fetchxml>
    <layoutxml>...</layoutxml>
    <querytype>2</querytype>
    ...
  </savedquery>
</savedqueries>
```

---

## 3. Server Source Analysis: How Dataverse Git Integration Serializes Components

### 3.1. The Canonical Payload Generation Pipeline

Analysis of decompiled server code reveals the **exact pipeline** Dataverse uses to create the "canonical form" of a component for Git:

```
SourceControlComponentService.GenerateComponentPayload()
  → ExportXml.RunExportComponentVersionFile(componentName, componentId)
    → RootExportHandler.RunExportComponentVersionFile()
      → BuildSolutionComponentEntitySetForSourceControl()
      → BuildComponentTableForSourceControl()
      → AddExportHandlersForVersioningExport()
      → Each ExportHandler.Export(xmlDocument) → populates customization XML
      → handler._filesToExportToSourceControl → per-file byte[] payloads
      → CrmZip → ZIP file containing all component files
```

**Critical insight:** The payload is a **ZIP file** containing XML/JSON/YAML files — the same format that `ExportSolution` produces. Each component is exported using the standard `ExportHandler` machinery, producing the same XML that goes into `customizations.xml` and solution ZIPs.

### 3.2. The Packer Architecture (Git → Dataverse Direction)

When pulling from Git, `SourceControlSolutionPackager` reassembles files into a solution ZIP using `IComponentPacker` implementations:

```csharp
// Platform components → concatenated into customizations.xml
_concatCustomizationsXmlComponents = {
    [1]   = "Entities",        // Entity
    [80]  = "AppModules",      // AppModule
    [62]  = "AppModuleSiteMaps",
    [29]  = "Workflows",       // Workflow/Flow
    [10]  = "EntityRelationships",
    [9]   = "optionsets",      // OptionSet
    [432] = "EntityImageConfigs",
    [431] = "AttributeImageConfigs",
    [20]  = "Roles",           // Security Role
    [401] = "AIModels",
    [400] = "AITemplates",
    [91]  = "SolutionPluginAssemblies",
    [92]  = "SdkMessageProcessingSteps",
    [372] = "Connectors",
    [61]  = "WebResources"
};
```

Specialized packers for sub-components:
- **`AttributePacker`** → `EntitySubComponentPacker` → nests attributes under parent Entity in `customizations.xml`
- **`SystemFormPacker`** → splits dashboards (→ `CustomizationXmlConcatPacker`) vs entity forms (→ `FormXmlPacker` → `EntitySubComponentPacker`)
- **`SavedQueryPacker`** → `EntitySubComponentPacker` → nests under Entity/SavedQueries
- **`ScfPacker`** → adds individual files + auto-generates parent stub XML with export key attributes

### 3.3. SCF Component Handling (ScfPacker)

The `ScfPacker` implementation reveals how SCF components are serialized:

1. Each SCF component file goes directly into the ZIP as-is (`filesToContent.Add(filePath, payload)`)
2. A **parent stub XML** is auto-generated with export key attributes:
   ```xml
   <parentEntityName attr1="value1" attr2="value2" />
   ```
3. Parent entity is discovered via `GetParentChildRelationship()` → `ComponentDefinitionCache`
4. File extensions: `.xml`, `.json`, or `.yml` (YAML when `EnableSourceControlYamlConversion` is on)

### 3.4. File Format Conversion (SourceControlHelper)

The server supports 3 file formats, controlled by org settings:

```csharp
// Format codes: 0 = XML (default), 1 = JSON, 2 = YAML
SerializeFile(xmlNode, format):
  case 0: XElement.Parse(node.OuterXml).ToString()     // Pretty-printed XML
  case 1: JsonConvert.SerializeXmlNode(node, Indented)  // XML→JSON via Newtonsoft
  case 2: ConvertJsonToYaml(SerializeXmlNode(node))     // XML→JSON→YAML

DeserializeFile(content, format):
  case 0: CreateXmlDocument(content)                     // Parse XML
  case 1: JsonConvert.DeserializeXmlNode(content)        // JSON→XML via Newtonsoft
  case 2: ConvertYamlToJson(content) → DeserializeXmlNode // YAML→JSON→XML
```

**Key insight:** The canonical internal representation is always **XML**. JSON and YAML are just serialization formats applied on top. This means local files in any of the 3 formats are structurally equivalent.

### 3.5. File Comparison (FileComparer)

The server's `FileComparer` compares solution ZIPs at the **byte level**:

```csharp
CompareZipParts(baseZip, upgradeZip, uri):
  baseBytes = baseZip.GetPart(uri)
  upgradeBytes = upgradeZip.GetPart(uri)
  return baseBytes.SequenceEqual(upgradeBytes)
```

Per-component file paths are constructed from:
- **File-type attributes:** WebResource → `FileName`, Workflow → `XamlFileName`/`JsonFileName`, PluginAssembly → `FileName`
- **Memo-type attributes:** Serialized text blobs
- **Image-type attributes:** Binary image data
- **Export key-based paths:** `<parentPrefix>/<componentCollection>/<exportKey>/<attribute>/<filename>`

### 3.6. The SourceControlExporter Flow (Export to Git)

```
SourceControlExporter.ExportToLocalBranch()
  → Reads solutioncomponentfile records (objectid, path, text, operation)
  → Groups by solutioncomponenttype
  → For FileScope=Global (2): concatenates all component XML into single file
      → Sorts via SourceControlHelper.SortComponentSetNode() (deterministic ordering)
      → Writes to Assets/<entitySetName>.xml
  → For FileScope=Individual (1): writes each component as its own file
      → Path from ExportKeyHelper.CalculateFilePath()
      → Recurses into child relationships (parent-child hierarchy)
  → Compares with existing Git files → Create/Update/Delete operations
```

---

## 4. Comparison: Server Payload vs Local Files vs Layer JSON

### Three Representations of the Same Data

| Representation | Format | Source | Fidelity |
|---|---|---|---|
| **`msdyn_componentjson`** (Layer API) | Flat JSON key-value bag | `msdyn_componentlayers` API | Metadata-only; forms/views have XML strings embedded; no binary content |
| **ExportSolution ZIP** (SolutionPackager) | Hierarchical XML in ZIP | `ExportSolution` action → SolutionPackager unpack | Full fidelity — everything including binaries |
| **Git payload** (Source Control) | ZIP of XML/JSON/YAML files | `RunExportComponentVersionFile` | Full fidelity — same export handlers as ExportSolution |

### Critical Finding: Git Payload ≡ Export Solution Payload

Both the Git integration and `ExportSolution` use the **same `ExportHandler` pipeline** to generate component XML. The Git payload is literally `ExportXml.RunExportComponentVersionFile()` which calls the same export handlers. This means:

1. **Git source control files and SolutionPackager files represent the same data** — they're generated by the same code paths
2. **If we export + unpack, we get format-identical output to what SolutionPackager produces locally**
3. The only difference is the file organization (SolutionPackager has its own folder structure vs Git integration's `solutioncomponentfile`-based paths)

### Why Layer JSON Is the Wrong Comparison Target

The `msdyn_componentjson` is a **runtime database view** — a serialized `Entity` record. It's NOT the canonical export format. The canonical format is what `ExportHandler` produces (XML in customizations.xml or individual files). Trying to compare layer JSON with SolutionPackager XML means bridging two completely different serialization schemes.

---

## 5. Revised Difficulty Ratings (Informed by Server Source)

| Component Type | Direct Layer JSON Diff | Temp Export + SolutionPackager Diff | Notes |
|---|---|---|---|
| **Entity definition** | **Hard** — 135 flat keys vs deeply nested XML. Server uses `EntityExportHandler` to produce the XML; no public mapping exists. | **Easy** — format-identical after unpack | Entity.xml embeds attributes inline; server has them as separate records |
| **Form XML** | **Medium** — `formxml` in JSON matches inner XML, but `FormXmlPacker` applies additional nesting (entity → FormXml → forms[@type] → systemform). Not a simple extract. | **Easy** — format-identical | Server's `SystemFormPacker` splits dashboards vs entity forms; export handles this |
| **View (SavedQuery)** | **Medium** — `fetchxml`/`layoutxml` strings match, but `SavedQueryPacker` nests under Entity → SavedQueries. | **Easy** — format-identical | |
| **Attribute** | **Hard** — 71 flat keys; locally embedded inside Entity.xml `<attributes>` section. `AttributePacker` nests under entity node. | **Easy** — format-identical | No separate local files |
| **OptionSet** | **Medium** — simpler structure but `CustomizationXmlConcatPacker` concatenates into `optionsets` root | **Easy** — format-identical | |
| **WebResource** | **Impossible via layers** — layer has metadata only, not binary content. `FileComparer` uses `FileName` attribute to locate file in ZIP. | **Easy** — direct byte comparison | Must use `webresourceset` API or export |
| **Workflow/Process** | **Hard** — layer has metadata; actual XAML/JSON is in separate file. `FileComparer.InitializeComponentToFilePathAttribute` maps to `XamlFileName`, `JsonFileName`. | **Easy** — format-identical | Cloud flows = JSON; classic = XAML |
| **Plugin Assembly** | **Impossible via layers** — binary DLL, only metadata in layer. `FileComparer` maps to `FileName`. | **Easy** — byte comparison | |
| **SCF component** | **Medium-Hard** — `ScfPacker` adds parent stub XML and uses export keys. Type codes are runtime-assigned. | **Easy** — format-identical | Must resolve type codes by name (from `solutioncomponentdefinitions`) |
| **Canvas App** | **Impossible via layers** — `.msapp` binary package. `CanvasAppPacker` has separate handling. | **Easy** — binary comparison | |

---

## 6. Approach Assessment (Revised)

### Approach A: Direct Layer JSON ↔ Local File Comparison

**Verdict: NOT VIABLE for general-purpose comparison.**

The server source confirms this is fundamentally the wrong approach. `msdyn_componentjson` is a database view, not an export artifact. The canonical component form is produced by `ExportHandler` → XML. There is no public mapping from layer JSON keys to export XML elements, and several component types (WebResource, PluginAssembly, CanvasApp) have NO content in layers at all.

**Still useful for:** Layer inspection, "who changed this?", conflict detection.

### Approach B: Temp Solution Export + SolutionPackager Unpack + File Diff

**Verdict: RECOMMENDED — and validated by server architecture.**

The server source confirms that `ExportSolution` and Git integration use the **same export pipeline**. This means:
- Export + SolutionPackager unpack produces **exactly** the format developers work with locally
- The comparison is **apples-to-apples** by design
- All component types are covered, including binaries

### Approach C: Replicate Git Payload Generation Client-Side

A more ambitious alternative: instead of export + unpack, replicate what `RunExportComponentVersionFile` does — but from the client side using OData.

**Why this won't work:** The export pipeline runs server-side, uses internal metadata caches (`AbstractMetadataCache`, `ComponentDefinitionCache`), and calls 15+ specialized `ExportHandler` implementations. We can't replicate this from OData.

### Approach D: Use Dataverse Git Integration Directly

If the environment has Git integration enabled, we could read from `solutioncomponentfile` records directly — these contain the canonical payload already serialized.

**Pros:** No export needed; payload is pre-generated.
**Cons:** Requires Git integration to be enabled; not universally available; adds dependency on a preview feature.

---

## 7. Recommendation: Approach B (Temp Export Diff)

**Approach B is clearly the best option** because:

1. **Format parity is guaranteed by architecture.** The same `ExportHandler` pipeline produces both the export ZIP and the Git payload. SolutionPackager unpacking of an export ZIP gives the exact format developers have locally.
2. **Universal coverage.** Works for all 100+ component types — platform and SCF — without per-type code.
3. **Binary content included.** WebResources, plugins, canvas apps — all in the export.
4. **Correctness.** SolutionPackager is the authoritative unpacker. Any comparison using it is inherently correct.
5. **The server itself uses this approach.** `FileComparer.CompareZipParts` compares exports at the byte level. We're following the same pattern.

### Performance Considerations

- **Model solution** (1 entity, 1 root component): Export ~2-5 seconds. Fast.
- **Basic solution** (37 entities, 38 root components): Export ~10-30 seconds. Acceptable.
- **Single-component comparison:** Temp solution with one component → export → fast (~2-5 sec).
- **Full-solution comparison:** Export the named solution directly. No temp solution needed.

### Recommended Architecture

```
txc solution compare --solution Model --source ./src/Solutions.Model
  │
  ├── 1. ExportSolution("Model", unmanaged) → ZIP bytes
  │      (Web API action: POST /api/data/v9.2/ExportSolution)
  │
  ├── 2. SolutionPackager.Extract(ZIP → tempDir)
  │      (Using ISolutionPackagerService from Phase 0-1)
  │
  ├── 3. FileDiff(tempDir, ./src/Solutions.Model)
  │      ├── XML files: XDocument.DeepEquals or normalized string compare
  │      ├── JSON files: JToken.DeepEquals
  │      ├── Binary files: byte[] SequenceEqual (following FileComparer pattern)
  │      └── Report: Added / Modified / Deleted files
  │
  └── 4. Cleanup tempDir
```

For single-component comparison:
```
txc solution compare --entity fin_mytable --source ./src/Solutions.Model
  │
  ├── 1. CreateSolution("_txc_compare_<timestamp>")
  ├── 2. AddSolutionComponent(Entity, fin_mytable, IncludeSubcomponents)
  ├── 3. ExportSolution → SolutionPackager → Diff (same as above)
  ├── 4. DeleteSolution("_txc_compare_<timestamp>")
  └── 5. Cleanup
```

### Diff Engine Design (Inspired by Server FileComparer)

Following the server's `FileComparer` pattern:

```csharp
// 1. Binary comparison for known binary types
private static readonly HashSet<string> BinaryExtensions = { ".msapp", ".dll", ".png", ".gif", ".jpg", ".ico" };
if (BinaryExtensions.Contains(ext))
    return File.ReadAllBytes(local).SequenceEqual(File.ReadAllBytes(server));

// 2. XML normalization for XML files (following SourceControlHelper.SerializeFile pattern)
if (ext == ".xml")
    return XDocument.Load(local).DeepEquals(XDocument.Load(server));
    // Or: normalize whitespace + attribute ordering, then string compare

// 3. JSON structural comparison
if (ext == ".json")
    return JToken.DeepEquals(JToken.Parse(localContent), JToken.Parse(serverContent));

// 4. Text comparison for everything else
return File.ReadAllText(local) == File.ReadAllText(server);
```

---

## 8. Prerequisites from Phase 0-1

| Prerequisite | Status | Needed For |
|---|---|---|
| **SolutionPackager integration** (`ISolutionPackagerService`) | Planned (Phase 0-1) | Unpacking exported solutions |
| **Authenticated Dataverse connection** | ✅ Already working (`txc env data query odata`) | All API calls |
| **Solution export** (`ExportSolution` Web API action) | Needs implementation | Getting server-side solution ZIP |
| **Solution CRUD** (create/delete temp solution) | Needs implementation | Single-component comparison |
| **`AddSolutionComponent` action** | Needs implementation | Adding components to temp solution |
| **File diff engine** | Needs implementation | Comparing unpacked files |

---

## 9. Estimated Effort Breakdown

| Task | Effort | Dependencies |
|---|---|---|
| SolutionPackager integration (from Phase 0-1) | 3-5 days | PAC CLI/DLL discovery |
| `ExportSolution` Web API action | 1 day | Auth (done) |
| Temp solution create/delete + `AddSolutionComponent` | 1-2 days | Auth (done) |
| File diff engine (XML-aware, JSON-aware, binary) | 2-3 days | None |
| CLI command + UX (output formatting, filtering) | 1-2 days | All above |
| SCF type code resolution | 0.5 days | Auth (done) |
| Testing + edge cases | 2-3 days | All above |
| **Total** | **~2-3 weeks** | SolutionPackager from Phase 0-1 |

---

## 10. Layer JSON — Complementary Use Cases

Even though Approach B is recommended for structural comparison, the `msdyn_componentlayers` API is valuable for complementary scenarios:

1. **"Who changed this?"** — `msdyn_changes` shows per-layer deltas; `msdyn_solutionname` + `msdyn_order` shows the solution stack
2. **Quick property inspection** — Check a single property value without full export
3. **Layer conflict detection** — Multiple layers modifying the same component (detected via `msdyn_order > 1`)
4. **Drift detection (lightweight)** — Compare `modifiedon` timestamps before doing expensive exports
5. **Change attribution** — Which solution layer introduced `isaudited = true`?

These use cases are **informational** (not diff-based) and don't need format mapping.

---

## 11. SCF-Specific Considerations

From the server source analysis:

1. **Type code resolution is mandatory.** SCF type codes are runtime-assigned and can differ between environments. Always resolve by `solutioncomponentdefinitions.name`, never by `objecttypecode`.

2. **`ScfPacker` generates parent stubs.** When comparing SCF components, the local files may include a parent stub XML (e.g., `<parentEntity attr="value" />`) that needs to be expected in the diff.

3. **File scope varies per SCF type.** The `solutioncomponentconfiguration` entity's `filescope` setting (0=None, 1=Individual, 2=Global) determines whether components are individual files or concatenated. The diff engine must handle both.

4. **File format varies.** SCF components may be in XML, JSON, or YAML depending on org settings and `solutioncomponentconfiguration.fileformat`. The diff engine should normalize (e.g., parse both sides to XML/DOM before comparing).

---

## 12. Open Questions

1. **`ExportSolution` Web API action availability.** Confirmed available: `POST /api/data/v9.2/ExportSolution` with `{ "SolutionName": "...", "Managed": false }`. Returns base64-encoded ZIP.
2. **Temp solution cleanup on failure.** Need robust try/finally to avoid orphan `_txc_compare_*` solutions. Consider a cleanup command or auto-detect stale temp solutions.
3. **Large solution performance.** For solutions with 500+ components, export + unpack could take minutes. Consider:
   - Progress reporting during export
   - Caching the last export for quick re-comparison
   - Incremental comparison using `modifiedon` timestamps as a pre-filter
4. **Managed vs unmanaged.** Always export unmanaged to match local source files. Managed comparison is a separate use case (deployment verification).
5. **XML normalization.** SolutionPackager output should be deterministic, but minor differences (XML declaration, encoding, BOM) may cause false positives. Use `XDocument.DeepEquals` or normalize before string comparison — following the server's `SourceControlHelper.SerializeFile` pattern of `XElement.Parse(node.OuterXml).ToString()`.
6. **Dataverse Git integration as alternative data source.** If the target environment has Git integration enabled, `solutioncomponentfile` records contain pre-serialized payloads. This could be a fast path for comparison without export — worth investigating as a future optimization.
