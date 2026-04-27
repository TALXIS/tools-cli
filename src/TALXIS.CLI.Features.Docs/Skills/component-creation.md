# Creating Dataverse Components

## Key Concept

Scaffolding creates **local files** in your workspace — it does NOT create components in a live Dataverse environment. Changes are tracked in source control and can be reviewed before deployment.

## Workflow Chain

1. **`workspace_explain`** — understand what exists in the repo before creating anything
2. **`workspace_component_type_list`** — discover available component types
3. **`workspace_component_parameter_list`** — get required parameters for a component type
4. **`workspace_component_create`** — scaffold the component locally
5. **Build locally** to validate, then follow the [deployment workflow](deployment-workflow.md)

**Default convention:** Always pass `SolutionRootPath=Declarations` unless the user specifies a different solution project path.

## Composition Ordering

Scaffold components in dependency order:
1. **Entity** (table definition) — first
2. **Attributes** (columns) — after entity exists
3. **Relationships** — after both source and target entities exist
4. **Forms** — after entity and its attributes exist
5. **Views** — after entity and its attributes exist

## What NOT to Do

- ❌ Don't use `environment_entity_create` for development — it bypasses source control
- ❌ Don't scaffold a Form or View before the Entity and its Attributes exist — XML references will break
- ❌ Don't scaffold a Relationship if the target table hasn't been created yet
- ✅ Use environment tools only for inspection, troubleshooting, or emergency fixes

See also: [project-structure](project-structure.md), [schema-management](schema-management.md)
