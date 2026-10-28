# Creating Dataverse Components

## Key Concept

Scaffolding creates **local files** in your workspace — it does NOT create components in a live Dataverse environment. Changes are tracked in source control and can be reviewed before deployment.

## Workflow Chain

1. **Read the workspace** — the solution file (`.slnx`, or `.sln` in older repos; `dotnet sln list` lists its projects) and each project's `ProjectType` show what exists before you create anything. Never assume project names or folders
2. **`txc component type list`** — discover available component types
3. **`txc workspace component parameter list <template>`** — get required parameters for a component type
4. **`txc workspace component create <template> --output <dir> --param <key>=<value>`** — scaffold the component locally. `--output` is required: the folder the component is scaffolded into (for `pp-plugin`: the project folder; for solution components, the folder of the solution project that owns them). Repeat `--param` per parameter, for example:
   ```
   txc workspace component create pp-entity-form --output src/Solutions.UI --param FormType=main --param EntitySchemaName=prefix_table
   ```
   (`src/Solutions.UI` is the default layout, where forms live; use the solution project the repository has.)
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
7. **Project Reference** — `dotnet add <solution-project> reference <separate-project>` from the solution project to the separate project, then build the solution project (`dotnet build <solution-project>`). The Build SDK auto-detects the project type and generates the registration data (assembly data.xml, web resource data.xml, etc.) during that build. For plugin assemblies and steps (`[PluginStep]` vs `pp-plugin-assembly-step`) see [plugin-development](plugin-development.md)
8. **Ribbon Buttons** (`pp-ribbon-button`) — after entity and script library exist. References the web resource via `LibraryLogicalName=prefix_name`
9. **Form Event Handlers** (`pp-form-event-handler`) — after entity, form, and script library exist. References via `libraryName=prefix_name.js` and `functionName=prefix_name.ClassName.methodName`

## Key Parameter Conventions

Where a name carries a prefix, use the customization prefix of the target solution: `<CustomizationPrefix>` in the solution project's `Other/Solution.xml`, or `txc environment publisher list` for a live environment. The examples below use `prefix_`.

- **`LogicalName`** (in pp-entity, pp-entity-attribute, pp-optionset-global, pp-app-model): The name **without** publisher prefix. The template adds the prefix automatically. Example: `warehouseitem`, not `prefix_warehouseitem`.
- **`EntitySchemaName`** (in pp-entity-attribute, pp-entity-form, pp-entity-view, pp-form-*): The entity name **with** publisher prefix. Example: `prefix_warehouseitem`.
- **`EntityLogicalName`** (in pp-sitemap-subarea, pp-app-model-component, pp-ribbon-*): The entity name **with** publisher prefix. Example: `prefix_warehouseitem`.
- **`AppName`** (in pp-sitemap-*, pp-app-model-component): The app module folder name **with** publisher prefix. Example: `prefix_warehouseapp`.
- **`ReferencedEntityName`** (in pp-entity-attribute for Lookup types): The target entity **with** publisher prefix. Example: `prefix_warehouseitem`.
- **`Behavior`** (in pp-entity): Use `New` when creating the entity definition for the first time. Use `Existing` to add a reference to an entity owned by another solution (e.g., adding forms/views in a UI solution for an entity defined in the DataModel solution).

## Prerequisites

Some templates use C# file-based apps (`.cs`) for code generation. These run natively with .NET 10 SDK — no additional tools required.

## What NOT to Do

- ❌ Don't use `txc environment entity create` for development — it changes the live environment and bypasses source control
- ❌ Don't scaffold a Form or View before the Entity and its Attributes exist — XML references will break
- ❌ Don't scaffold a Relationship if the target table hasn't been created yet
- ❌ Don't run `pp-plugin-assembly` unless the solution doesn't register the assembly itself (`GeneratePluginAssembly=false` or an older Build SDK), and then build the plugin project first (it reads the compiled DLL) — see [plugin-development](plugin-development.md)
- ❌ Don't run `pp-webresource` for script libraries — the Build SDK auto-generates the web resource when a ScriptLibrary project is referenced via `dotnet add reference`
- ❌ Don't call `npm install` or `npm run build` manually for script libraries — `dotnet build` handles everything
- ❌ Don't manually create assembly data.xml for plugins or workflow activities — current Build SDKs generate them from project references (see [plugin-development](plugin-development.md) for the exceptions)
- ✅ Use environment tools only for inspection, troubleshooting, or emergency fixes

See also: [project-structure](project-structure.md), [schema-management](schema-management.md)
