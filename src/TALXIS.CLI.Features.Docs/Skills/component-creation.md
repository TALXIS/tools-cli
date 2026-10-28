# Creating Dataverse Components

Scaffolding creates **local files** in the workspace, not components in a live environment; changes are reviewed in source control before deployment.

## Workflow Chain

1. **Read the workspace**: the solution file's projects (`dotnet sln list`) and each project's `ProjectType`. Never assume project names or folders.
2. `txc component type list` — available component types.
3. `txc workspace component parameter list <template>` — parameters of a type.
4. `txc workspace component create <template> --output <dir> --param <key>=<value>` — `--output` is required: the folder the component is scaffolded into (for `pp-plugin`, the project folder; for solution components, the owning solution project). Repeat `--param` per parameter:
   ```
   txc workspace component create pp-entity-form --output src/Solutions.UI --param FormType=main --param EntitySchemaName=prefix_table
   ```
5. Validate: `dotnet build`, then from each solution project directory `dotnet msbuild -t:InitializeSolutionPackagerWorkingDirectory;ValidateSolutionComponentSchema` (checks the solution XML against XSD schemas).
6. Follow the [deployment workflow](deployment-workflow.md).

## Composition Ordering

Scaffold in dependency order:
1. **Entity**, then its **Attributes**
2. **Relationships** — after both entities exist
3. **Forms** and **Views** — after the entity and its attributes
4. **Separate projects** (Plugin, ScriptLibrary, WorkflowActivity, CodeApp, PCF) — their own `pp-*` template creates a `.csproj`; reference it from the solution project ([project-structure](project-structure.md)) and build that. Plugins: [plugin-development](plugin-development.md). Script libraries need no `pp-webresource` or manual `npm`.
5. **Ribbon Buttons** (`pp-ribbon-button`) — after entity and script library; `LibraryLogicalName=prefix_name`
6. **Form Event Handlers** (`pp-form-event-handler`) — after entity, form and script library; `libraryName=prefix_name.js`, `functionName=prefix_name.ClassName.methodName`

## Parameter Conventions

Use the customization prefix of the target solution (`<CustomizationPrefix>` in the solution project's `Other/Solution.xml`); examples use `prefix_`.

| Parameter | Used in | Value |
|---|---|---|
| `LogicalName` | pp-entity, pp-entity-attribute, pp-optionset-global, pp-app-model | **without** prefix (the template adds it): `warehouseitem` |
| `EntitySchemaName` | pp-entity-attribute/-form/-view, other pp-form-* fragments | **with** prefix: `prefix_warehouseitem` |
| `EntityLogicalName` | pp-ribbon-*, pp-sitemap-subarea, pp-app-model-component, pp-form-event-handler, pp-form-subgrid | same |
| `LookupTarget` | pp-entity-attribute (Lookup) | target table, same |
| `AppName` | pp-sitemap-*, pp-app-model-component | app module folder **with** prefix: `prefix_warehouseapp` |
| `Behavior` | pp-entity | `New` defines the entity; `Existing` references an entity owned by another solution (forms/views in a UI solution) |

Some templates run C# file-based apps (`.cs`) that need the .NET 10 SDK.

## What NOT to Do

- ❌ Don't use `txc environment entity create` for development: it changes the live environment and bypasses source control. Use environment commands for inspection, troubleshooting or emergency fixes.
- ❌ Don't scaffold a Form, View or Relationship before the entities and attributes it references exist.

See also: [project-structure](project-structure.md), [schema-management](schema-management.md)
