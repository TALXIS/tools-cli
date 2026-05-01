# Schema Workflow — Decision Logic

<!-- Internal reasoning skill: contains ONLY workflow sequencing and tool selection. -->
<!-- For step-by-step instructions and parameters, see the public component-creation skill. -->

## Mandatory Sequencing

### New table with columns
```
1. workspace_component_create (Entity)        — table MUST exist before columns
2. workspace_component_create (Attribute) ×N   — one call per column
3. workspace_component_create (Form)           — AFTER table + columns exist
4. workspace_component_create (View)           — AFTER table + columns exist
```
→ NEVER scaffold Form/View before the Entity and Attributes exist — XML references will break

### Adding to existing table (same solution)
```
1. workspace_explain                           — confirm table exists, find SolutionRootPath
2. workspace_component_create (Attribute/Form/View)
3. Build locally to validate
```
→ SKIP step 1 ONLY if you already know the project structure from prior context

### Adding views/forms for an entity from another solution
```
1. workspace_component_create (Entity, Behavior=Existing)  — creates entity folder reference, no full metadata
2. workspace_component_create (Form/View)                  — scaffolds into the referenced entity folder
3. Build locally to validate
```
→ Use this when e.g. Solutions.UI needs forms/views for an entity defined in Solutions.DataModel
→ Behavior=Existing creates the entity folder structure without duplicating the entity definition

### Creating a relationship
```
1. Confirm BOTH tables exist (workspace_explain or prior scaffolding)
2. workspace_component_create (Relationship)
3. Build to validate referential integrity
```
→ NEVER scaffold a relationship if the target table doesn't exist yet — will produce invalid XML

## SolutionRootPath Selection
→ DEFAULT: `Declarations` (convention for schema components)
→ OVERRIDE: only if user specifies a different solution project name
→ IF UNSURE: run `workspace_explain` to discover available solution projects

## When to Use Workspace vs Environment Schema Tools
→ Creating/modifying schema for development: `workspace_component_create` (local)
→ Inspecting what's deployed: `environment_entity_list`, `environment_entity_attribute_list` (live)
→ Emergency fix in non-prod: `environment_entity_attribute_create` (live, acceptable)
→ Prototyping before codifying: environment tools acceptable, but codify locally afterward

## Anti-Patterns
- ❌ Scaffolding a Form before its Entity exists → broken XML references
- ❌ Forgetting to build after scaffolding → XML errors caught late at import
- ❌ Using environment tools for development → changes not in source control
- ❌ Scaffolding into wrong solution project → component ownership issues
