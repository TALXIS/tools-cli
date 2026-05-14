# Creating Dataverse Components

## Key Concept

Scaffolding creates **local files** in your workspace — it does NOT create components in a live Dataverse environment. Changes are tracked in source control and can be reviewed before deployment.

## Workflow Chain

1. **`workspace_explain`** — understand what exists in the repo before creating anything
2. **`workspace_component_type_list`** — discover available component types
3. **`workspace_component_parameter_list`** — get required parameters for a component type
4. **`workspace_component_create`** — scaffold the component locally
5. **Build locally** to validate: `dotnet build`
6. **Run schema validation** to catch XML errors early:
   ```
   dotnet msbuild -t:InitializeSolutionPackagerWorkingDirectory;ValidateSolutionComponentSchema
   ```
   This validates all solution XML files against XSD schemas. Run it from each solution project directory.
7. Follow the [deployment workflow](deployment-workflow.md)

## Composition Ordering

Scaffold components in dependency order:
1. **Entity** (table definition) — first
2. **Attributes** (columns) — after entity exists
3. **Relationships** — after both source and target entities exist
4. **Forms** — after entity and its attributes exist
5. **Views** — after entity and its attributes exist
6. **Separate projects** (Plugin, ScriptLibrary, WorkflowActivity, CodeApp, PCF) — scaffold with their respective `pp-*` template, creates a separate .csproj
7. **Project Reference** — `dotnet add reference` from solution project to the separate project. The Build SDK auto-detects the project type and handles registration (assembly data.xml, web resource data.xml, etc.) during `dotnet build`
8. **Ribbon Buttons** (`pp-ribbon-button`) — after entity and script library exist. References the web resource via `LibraryLogicalName=prefix_name`
9. **Form Event Handlers** (`pp-form-event-handler`) — after entity, form, and script library exist. References via `libraryName=prefix_name.js` and `functionName=prefix_name.ClassName.methodName`

## Key Parameter Conventions

- **`LogicalName`** (in pp-entity, pp-entity-attribute, pp-optionset-global, pp-app-model): The name **without** publisher prefix. The template adds the prefix automatically. Example: `warehouseitem`, not `udpp_warehouseitem`.
- **`EntitySchemaName`** (in pp-entity-attribute, pp-entity-form, pp-entity-view, pp-form-*): The entity name **with** publisher prefix. Example: `udpp_warehouseitem`.
- **`EntityLogicalName`** (in pp-sitemap-subarea, pp-app-model-component, pp-ribbon-*): The entity name **with** publisher prefix. Example: `udpp_warehouseitem`.
- **`AppName`** (in pp-sitemap-*, pp-app-model-component): The app module folder name **with** publisher prefix. Example: `udpp_warehouseapp`.
- **`ReferencedEntityName`** (in pp-entity-attribute for Lookup types): The target entity **with** publisher prefix. Example: `udpp_warehouseitem`.
- **`Behavior`** (in pp-entity): Use `New` when creating the entity definition for the first time. Use `Existing` to add a reference to an entity owned by another solution (e.g., adding forms/views in a UI solution for an entity defined in the DataModel solution).

## Prerequisites

Some templates use C# file-based apps (`.cs`) for code generation. These run natively with .NET 10 SDK — no additional tools required.

## What NOT to Do

- ❌ Don't use `environment_entity_create` for development — it bypasses source control
- ❌ Don't scaffold a Form or View before the Entity and its Attributes exist — XML references will break
- ❌ Don't scaffold a Relationship if the target table hasn't been created yet
- ❌ Don't run `pp-plugin-assembly` before building the plugin project (`dotnet build`) — it reads the compiled DLL
- ❌ Don't run `pp-webresource` for script libraries — the Build SDK auto-generates the web resource when a ScriptLibrary project is referenced via `dotnet add reference`
- ❌ Don't call `npm install` or `npm run build` manually for script libraries — `dotnet build` handles everything
- ❌ Don't manually create assembly data.xml for plugins or workflow activities — the Build SDK auto-generates them from project references
- ✅ Use environment tools only for inspection, troubleshooting, or emergency fixes

See also: [project-structure](project-structure.md), [schema-management](schema-management.md)
