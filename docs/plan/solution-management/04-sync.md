# Phase 4 — Client-Side Workspace Sync & Patch Generation (Future)

> Design approach: **Export + SolutionPackager + file diff** — generic, zero per-type code, ~5-15s per comparison.
> Research artifacts: [research/fast-compare-design.md](./research/fast-compare-design.md) (per-type approach — rejected as too much code), [research/comparison-feasibility-report.md](./research/comparison-feasibility-report.md) (initial feasibility study)

## Vision

A developer modifies a form or workflow in their local unpacked solution files (Git workspace). The CLI computes what changed compared to the server, generates a minimal patch solution, and imports only the delta — making the sync near-instant instead of importing the entire solution.

This is the client-side equivalent of Dataverse's server-side Smart Diff / patch generator.

## Dataverse Server Architecture (from decompiled source)

### Smart Diff Engine (`Microsoft.Xrm.SolutionsFramework.dll`)

- **Algorithm:** Structural XML comparison (NOT byte-level) via graph nodes
- **Flow:** `XML → XMLConverter.Deserialize() → GraphNodeEntity property bags → GraphComparer.GetPatched() → minimal XML`
- **GraphComparer:** Per-property comparison on `Dictionary<string, ValueAndType>` bags
- **FileComparer:** Byte-level `SequenceEqual` only for binary components (WebResource, PluginAssembly, Workflow XAML)
- **Label handling:** Only compares base LCID language; structured comparison for OptionSet child nodes
- **Supported types:** Entity(1), Attribute(2), Relationship(10), EntityKey(14), EntityIndex(18), Role(20), DisplayString(22), View(26), Chart(59), Form(60), CustomControl(65)
- **Output:** Subset of upgrade graph containing only Created + Modified components

### Git Integration (`SourceControl*` across 4 assemblies)

- **Change detection:** Hash-based — `githashid` is the **Git blob SHA-1** (not computed by Dataverse, returned by ADO API)
- **DetermineAction algorithm:**
  | `isCommitted` | `gitHash == lastSyncHash` | Action |
  |---|---|---|
  | true | true | None |
  | true | false | Pull |
  | false | true | Push |
  | false | false | Conflict |
- **15+ IComponentPacker implementations:** Serialize components into solution ZIP from individual files
- **File format:** XML by default, YAML opt-in; canonical form is always XML
- **Payload storage:** Each component produces a ZIP containing XML/JSON/YAML files

### Component Hash Computation

Dataverse does NOT compute content hashes — `githashid` IS the Git blob `objectId` (SHA-1) returned by the ADO REST API. For a CLI without ADO, we need our own hashing:

**Proposed approach:**
1. Serialize component to canonical form (sorted XML, normalized whitespace)
2. Compute SHA-256 of the canonical bytes
3. Store hash alongside component path in local sync state
4. Compare against server state (retrieved via `layer show` → `msdyn_componentjson`)

### SCF Components Compatibility

SCF components use the same `msdyn_componentlayer` and `msdyn_solutioncomponentsummaries` APIs, so all inspection commands work uniformly. For diff/sync:

- SCF type codes are **runtime-assigned** and can differ between environments — always resolve by component name, not type code
- SCF component schema is defined by each product team (no static schema reference)
- SCF components get SmartDiff automatically (built into the framework)
- The `solutioncomponentdefinitions` API can enumerate all registered SCF types

### Key Server Files (for porting reference)

| File | What to Port |
|------|-------------|
| `GraphComparer.cs` | Core diff engine — property comparison |
| `FileComparer.cs` | Binary file comparison for zip parts |
| `XMLConverter.cs` | XPath-based XML → graph deserialization |
| `GraphNodeEntity.cs` | Property bag data structure |
| `GraphNodeDefinitionFactory.cs` | Component type → XPath registry |
| `EntityMinimumSetCalculator.cs` | Full vs minimal serialization |
| `SmartDiffEnabledComponentListProvider.cs` | Which types support smart diff |
| `ComponentDefinitionXml.cs` | All 119 component type definitions |
| `SourceControlSolutionPackager.cs` | Solution ZIP assembly from files |

## Implementation Strategy

### Phase A — Component State Retrieval

The commands from Phase 1 provide the building blocks:

1. **`sln component list <solution>`** → enumerate all components (type, objectId, name) in the target solution
2. **`comp layer show <component-id> --type Entity`** → retrieve active layer definition JSON for each component

### The Problem: Three Approaches, None Perfect

| Approach | Speed | Generic? | Fidelity | Problem |
|----------|-------|----------|----------|---------|
| Export + SolutionPackager + file diff | 5-30s | ✅ | Full | **Too slow** for inner dev loop |
| `msdyn_componentlayers` JSON comparison | ~50ms/comp | ❌ | Declarative only | Needs **per-type normalizers** (JSON ↔ XML format mismatch) |
| Direct entity table queries (formxml, fetchxml) | ~50ms/comp | Partial | Per-table | Different tables per component type |

**The fundamental tension:** Generic approaches are slow (export), fast approaches need per-type code (layer JSON or entity queries).

### Proposed Hybrid: Direct Entity Queries + File Comparison

Many Dataverse component types store their canonical definition in queryable entity columns — the SAME content that goes into solution XML:

| Component | Entity Table | Key Column(s) | Format |
|-----------|-------------|---------------|--------|
| Form | `systemform` | `formxml` | XML string — same as in solution ZIP |
| View | `savedquery` | `fetchxml`, `layoutxml` | XML strings |
| SiteMap | `appmodulesitemap` or `sitemap` | `sitemapxml` | XML string |
| Workflow | `workflow` | `xaml` or `clientdata` | XAML/JSON |
| WebResource | `webresource` | `content` | Base64 encoded |
| Entity metadata | `EntityDefinitions` | Full metadata | OData metadata API |
| AppModule | `appmodule` | `clienttype`, `uniquename`, etc. | Entity columns |

**The insight:** For the most commonly edited components (forms, views, sitemaps), we can query the entity table directly and get the SAME XML that SolutionPackager unpacks — without any format bridging. The local file contains `<form>...</form>`, the server `formxml` column contains the same XML.

**This is still per-component-type code**, but it's minimal — just a query + column name per type, not a full normalizer. And it covers the highest-value developer scenarios (form and view editing).

### What Needs More Research

This is the area with the most open questions. Before committing to an approach, we need to:

1. **Prototype** a form comparison (query `systemform.formxml`, compare with local FormXml file) and measure actual fidelity — are the XML strings identical or do they need normalization?
2. **Understand** how SolutionPackager transforms the raw entity data into files — does it add wrappers, reorder elements, strip attributes?
3. **Evaluate** whether the `comp export` command (temp solution + export + unpack for a single component) is fast enough for single-component comparison (likely 2-5 seconds — acceptable for "check this one form I just edited")
4. **Investigate** `StageSolutionUploadRequest` / `StageSolutionRequest` — these are the staging APIs used during import. Can we stage a local solution and get the server to compare it for us (leveraging the built-in SmartDiff)?

### Phase B — Diff Calculation (to be designed after prototyping)

The comparison command design depends on the prototype results above. Two possible paths:

**Path 1 — Direct entity queries (fast, some per-type code):**
- For each component the developer commonly edits (forms, views, sitemaps), query the entity table
- Compare the content column with the local file
- Generic fallback: `comp export` for types without a direct query path

**Path 2 — Leverage server-side SmartDiff:**
- `SolutionPackager.Pack` local files → ZIP
- `StageSolutionRequest` → server compares and returns diff metadata
- If the staging API exposes what changed without a full import, this is both generic and fast

### Phase C — Patch Generation

Once we know which files changed:
1. Pack only changed files into a minimal solution ZIP via `SolutionPackager.Pack`
2. Import via `sln import` with `OverwriteUnmanagedCustomizations=false` (triggers SmartDiff server-side)
3. Track sync state locally (`.txc/sync-state.db` — file hashes from last successful comparison)

### Phase D — Local Change Detection (Hash Cache)

For instant "did anything change locally?":
1. Hash every local solution file → store in `.txc/sync-state.db`
2. On subsequent runs: only re-hash files that `git status` reports as modified
3. Report changed files instantly — no server call needed for local-side detection
4. Server comparison only when user requests sync
