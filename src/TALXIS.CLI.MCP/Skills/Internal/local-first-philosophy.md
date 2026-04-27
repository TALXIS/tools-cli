# Local-First Decision Tree

<!-- Internal reasoning skill: contains ONLY tool-selection logic. -->
<!-- For tool descriptions and parameters, see the public skills. -->

## User wants to "create a table" / "add a column" / "create a form"
→ ALWAYS: `workspace_component_create` (local scaffold, instant, reversible)
→ NEVER: `environment_entity_create` or `environment_entity_attribute_create` (live env — slow, not in source control)
→ EXCEPTION: user explicitly says "on the live environment", "for troubleshooting", or "quick fix in dev"

## User wants to "check what tables/columns exist"
→ IF building/developing: `workspace_explain` (reads local project structure)
→ IF troubleshooting/comparing with live: `environment_entity_list` or `environment_entity_attribute_list` (needs profile)

## User wants to "deploy" or "push changes"
→ REQUIRED ORDER: build locally → `environment_solution_pack` → `environment_solution_import` → `environment_solution_publish`
→ NEVER skip the local build step
→ NEVER deploy directly to production — always target dev/test first

## User wants to "delete a table/column"
→ IF local: delete/edit the XML files directly
→ IF live env: `environment_component_dependency_delete_check` FIRST, then `environment_entity_delete`
→ NEVER delete in live without checking dependencies

## User wants to "see solution layers" or "check conflicts"
→ ALWAYS live environment tools: `environment_component_layer_list` → `environment_component_layer_show`
→ These are inspection-only — local workspace has no layer concept

## User wants to "query data" or "migrate data"
→ ALWAYS live environment tools — data exists only in environments
→ See data-migration-workflow for volume-based tool selection

## When to Prefer Local vs Live

```
Local workspace tools:                 Live environment tools:
├─ Creating schema                     ├─ Inspecting deployed state
├─ Modifying forms/views               ├─ Deploying solutions
├─ Writing plugin code                 ├─ Data operations (CRUD, migration)
├─ Organizing solution components      ├─ Layer/dependency troubleshooting
└─ Any "building something new"        └─ Emergency fixes in non-prod
```

## Anti-Patterns
- ❌ `environment_entity_create` for development → changes aren't in source control
- ❌ `environment_entity_attribute_create` to add columns → not tracked locally
- ❌ Deploying without building first → catch XML errors early
- ❌ Modifying production directly → always go through managed solution import
- ❌ Using environment tools "because it's faster" → local is always faster (instant vs 30s–5min)
