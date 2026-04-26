# Dataverse Server Source Analysis — Summary

Generated from deep-dive analysis of decompiled D365CE-server-9.2.25063.

## Key Assemblies Analyzed

| Assembly | Key Area |
|----------|----------|
| `Microsoft.Xrm.SolutionsFramework.dll` | Git abstraction, GraphComparer, FileComparer, XMLConverter |
| `Microsoft.Crm.ObjectModel.dll` | Solution services, SourceControl orchestration, component services |
| `Microsoft.Crm.Platform.Server.dll` | Layering engine (140 files), ComponentDefinition, SourceControl constants |
| `Microsoft.Crm.WebServices.dll` | Component packers (15+ packers for Git serialization) |
| `Microsoft.Crm.Tools.Core.ImportExportPublish.dll` | SmartDiff, ReserveComponent handlers, ComponentHelpers |
| `Microsoft.Crm.Setup.DiffBuilder.dll` | MetadataPatchService, DiffBuilder |
| `Microsoft.Crm.dll` | ChecksumGenerator (SHA-512) |

## Three Key Systems Discovered

### 1. Git Integration (Source Control)
- **IGitHelper** interface: `GetFiles()`, `CreateFile()`, `GetChangesForPull/Push()`, `GetShas()`
- **Change detection**: Hash-based (compares `githashid` vs `lastsynchashid`)
- **File format**: XML (default), JSON, or YAML — controlled by org setting
- **Packers**: 15+ `IComponentPacker` implementations serialize components into Git-friendly format
- **Sync state**: Tracked in `sourcecontrolcomponent` table with `githashid`, `lastsynchashid`, `iscommitted`, `action` columns
- **GitAction**: None/Push/Pull/Conflict — computed from hash comparison

### 2. Smart Diff / Server-Side Patch Generator
- **Algorithm**: Structural XML comparison, NOT byte-level
- **Flow**: XML → Graph (via XPath) → Property-by-property comparison → Minimal XML patch
- **GraphComparer**: Core diff engine, compares `GraphNodeEntity` property bags
- **FileComparer**: Byte-level `SequenceEqual` for binary components (WebResource, PluginAssembly, etc.)
- **Supported types**: Entity(1), Attribute(2), Relationship(10), EntityKey(14), EntityIndex(18), Role(20), DisplayString(22), View(26), Chart(59), Form(60), CustomControl(65)
- **Labels**: Only compare org's base language LCID
- **OptionSets**: Structured XML comparison (child nodes individually)
- **Output**: Minimal XML containing only Created + Modified components
- **Checksums (SHA-512) are NOT used in the diff pipeline** — direct comparison instead

### 3. Component Definition System
- **119 hardcoded types** in `ComponentDefinitionXml.Definition` + dynamic types from `SolutionComponentDefinition` table
- **Key properties**: `ComponentType`, `Name`, `ComponentXPath`, `PrimaryKeyName`, `HasParent`, `RootComponent`
- **Layering**: `BusinessComponentState` with ordered layer stack, merge logic for cross-layer attribute propagation
- **States**: Publish, Unpublish, Delete, UnpublishedDelete, Snapshot, Stage
- **rootcomponentbehavior**: 0=include all, 1=shell only, 2=no metadata

## Detailed reports available in agent conversation history.
