# SCF (Solution Component Framework) Research

## What is SCF?

SCF is the newer architecture that allows Microsoft product teams to add new component types to Dataverse without changing core platform code. SCF components are registered via `solutioncomponentdefinition` metadata and brought to environments dynamically by first-party solutions.

## Platform Components vs SCF Components

| Aspect | Platform Components | SCF Components |
|--------|-------------------|----------------|
| **Type codes** | Static, well-known, same everywhere | **Runtime-assigned** (typically > 1000), can differ between environments |
| **Resolution on import** | By type code | By **unique component name** |
| **Schema** | Fixed, documented | No static schema — each owner decides format (JSON or XML) |
| **Import validation** | Strict | Lax — some values only fail at runtime |
| **Source control readability** | Good (readable XML) | Poor — non-descriptive filenames, GUIDs, encoded properties |
| **SmartDiff** | Must be enabled per type over time | Built-in to the framework — automatic |

## Discovery API

```
GET {ORG}/api/data/v9.2/solutioncomponentdefinitions?$select=name,objecttypecode
```

Returns all registered SCF component types with runtime type codes. Compare across environments to see differences.

## SCF in Solution ZIP

SCF components are stored as **individual files** in the solution ZIP rather than in `customizations.xml`. The `ScfPacker` (from server source):
- Packs each component file directly into the ZIP
- Auto-generates a **parent stub XML** with export key attributes for parent-child relationships
- Supports `.xml`, `.json`, `.yml` parent file extensions

## SCF in Git Integration (Server Source)

The `ScfPacker` is one of 15+ `IComponentPacker` implementations in `SourceControlSolutionPackager`. For Git:
- SCF child entities get individual files
- Traditional components get concatenated into `customizations.xml`
- Selection is based on `IsChildEntity == true` in metadata

## Impact on CLI Commands

All inspection commands (`component list`, `layer list`, `dependency *`) work uniformly with SCF components because they use the same Dataverse APIs (`msdyn_solutioncomponentsummaries`, `msdyn_componentlayer`, `RetrieveDependentComponents`).

For the future diff/sync feature (Phase 4): SCF type codes must be resolved by **name** not **code** since they can differ between environments.

## Source

- Blog: https://blog.networg.com/dataverse-solution-component-types/
- Server code: `Microsoft.Crm.WebServices.dll/.../SourceControlIntegration/ScfPacker.cs`
- Server code: `Microsoft.Xrm.SolutionsFramework.dll/.../SolutionComponentFileHelper.cs`
