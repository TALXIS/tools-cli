# MCP/CLI Feedback — Agent Experience Report

Session: 2026-05-14 / 2026-05-16 — Warehouse Management demo (flow creation, deployment, data operations)

---

## 🔴 Critical Issues

### 1. `pp-flow` template generates broken file names
Template scaffolds files with literal `__publisher-prefix__` instead of the actual prefix:
```
__publisher-prefix___notifynewwarehouseitem-9fda81a5-...json
```
Had to manually rename to `tom_notifynewwarehouseitem-...json`. Every other template (`pp-entity`, `pp-entity-attribute`) resolves the prefix correctly — flow is the outlier.

### 2. `pp-solution` template sets `<Managed>2</Managed>` — should be `0`
Scaffolded Solution.xml has `Managed=2` which is an invalid/ambiguous value. Exported solutions from Dataverse use `Managed=0` (unmanaged) or `Managed=1` (managed). This caused silent issues during import — the error message pointed elsewhere (readonly publisher), making it very hard to diagnose.

### 3. `pp-solution` template uses old schema version `9.1`
Scaffolded `<ImportExportXml version="9.1.0.643" SolutionPackageVersion="9.1">` while the environment runs `9.2`. This mismatch may contribute to import failures for modern component types (ModernFlow). Should match current platform version or at least `9.2`.

### 4. Readonly publisher error is cryptic and misleading
```
Solution manifest import: FAILURE: Attempting to update a readonly publisher.
```
This gives zero clue about **what** is readonly or **why**. Is it managed? Is it a field mismatch? The agent spent many attempts trying different workarounds. Suggestion: include which publisher field conflicts and what the expected vs actual values are.

### 5. "Component not declared as root component" error persists after component is deleted
After deleting the solution container and even the workflow record itself, re-importing with the same GUID still failed with:
```
The import has failed because component {guid} of type 29 is not declared in the solution file as a root component.
```
The workflow didn't exist in the environment anymore (confirmed via SQL query returning 0 rows), yet the error persisted. Only generating a completely new GUID resolved it. This suggests Dataverse caches component metadata beyond the record's lifecycle, but the error message gives no hint of this.

---

## 🟡 Medium Issues

### 6. `environment_solution_import` from project path runs `dotnet build` which overwrites Solution.xml
When importing via `--solution-path src/Solutions.Logic` (project directory), the build system regenerates Solution.xml from `.csproj` properties, overwriting manual fixes. This is expected behavior but creates a confusing loop when debugging import issues — you fix Solution.xml, import from project path, and your fix is silently reverted. Should either:
- Document this clearly in the error output
- Or provide a `--skip-build` flag

### 7. Version generation fails silently without git history
Without commits, `GenerateVersionNumber` falls back to `0.0.20000.0` which may be lower than the environment version, causing import failure. The warning message is vague:
```
LocalBranchBuildVersionNumber is null, setting to default.
```
Should explicitly say: "No git commits found — version defaulting to 0.0.20000.0. This may be lower than the environment version."

### 8. `workspace_validate` scans `obj/` build artifacts and reports false positive duplicate GUIDs
Validation found 63 "errors" — all were the same GUID appearing in both `Entities/*/SavedQueries/{guid}.xml` and `obj/Debug/.../SavedQueries/{guid}.xml`. The `obj/` folder should be excluded from validation automatically (it's a build artifact).

### 9. `environment_solution_export` deletes `.csproj` file
Exporting a solution back to a project directory (`--output src/Solutions.Logic`) deleted the `.csproj` file. This breaks the local build system. Export should preserve project files that aren't part of the Dataverse solution payload, or at least warn before deleting them.

### 10. `environment_entity_attribute_create --stage` with `--max-length "200"` fails on apply
Staging accepted the string attribute, but apply failed with:
```
Invalid cast from 'System.String' to 'System.Nullable`1[System.Int32]'
```
The `max-length` parameter is typed as `string` in the schema but the backend expects an integer. Either the schema should declare it as `integer`, or the CLI should coerce it automatically.

### 11. Changeset `--strategy transaction` doesn't roll back schema operations
Despite using transaction strategy, 2 of 3 operations succeeded while 1 failed. For schema operations, "transaction" doesn't provide atomicity — this should be documented, or the strategy names should differ for schema vs data operations.

---

## 🟢 Minor / UX Suggestions

### 12. `workspace_component_parameter_list` requires `ShortName` but doesn't list available short names
If you don't know the exact template name, you can't discover it through this tool. Had to fall back to `dotnet new list | grep pp-`. Should either accept empty input to list all, or have a separate `workspace_component_type_list` that's discoverable via guide tools.

### 13. Guide tools sometimes return two full recipes for one query
Several guide calls returned duplicate recipes with slightly different angles. This consumes a lot of context tokens. Should deduplicate or return only the most relevant recipe.

### 14. `environment_component_url_open` requires `param` array format for IDs
The `param: ["id=guid"]` key-value-in-array pattern is non-obvious. A dedicated `--id` parameter would be more ergonomic.

### 15. Flow `.json.data.xml` template sets `<StateCode>1</StateCode>` (Active)
New flows should default to Draft (`StateCode=0`, `StatusCode=1`) since they can't be activated without connection references configured in the target environment. Setting them as Active in the template creates a false expectation.

### 16. No `--solution` parameter on `environment_entity_attribute_create`
When creating attributes on system entities (like `account`), there's a `--solution` parameter but it's not clear which solution the attribute gets added to. Should default to the active solution or require explicit specification.

---

## 💡 What Worked Great

- **Progressive disclosure via guide tools** — discovering the right operation from natural language works very well
- **`environment_data_bulk_create`** — 1000 records created flawlessly in a single call
- **`data_package_export`** — clean CMT export once the schema was right
- **`environment_component_layer_list`** — instant insight into solution layering
- **`workspace_validate`** — fast local validation (once obj/ is cleaned)
- **Changeset staging** (`--stage` then `changeset apply`) — great pattern for batching schema changes
- **`environment_solution_export` + unpack** — round-trip export/unpack works smoothly
