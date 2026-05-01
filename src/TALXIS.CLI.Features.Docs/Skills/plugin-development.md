# Plugin & Custom Code Development

## Key Concept

Plugins are server-side .NET classes that execute custom business logic in response to Dataverse events. All plugin scaffolding uses `txc` workspace tools — this keeps everything in source control and locally reviewable before deployment.

## Plugin Project Structure

- Convention: `Plugins.{Domain}/` in `src/` (e.g., `Plugins.Sales`, `Plugins.Warehouse`)
- `.csproj` requires: `ProjectType=Plugin`, `SignAssembly=true`, target `net462`
- Plugin classes follow `{Action}{Entity}Plugin.cs` naming (e.g., `ValidateContactPlugin.cs`)

## Registration Chain

Plugin registration is a 3-step chain. **Order matters** — each step depends on the previous one.

1. **Create Plugin Project** → `workspace_component_create` with `Type: pp-plugin`
2. **Write plugin classes** extending `PluginBase` (one `.cs` file per plugin)
3. **Build the plugin project** → `dotnet build src/Plugins.{Domain}` — this MUST succeed before step 4
4. **Register Assembly** → `workspace_component_create` with `Type: pp-plugin-assembly`
   - Requires the plugin to be built first (reads compiled DLL for metadata)
5. **Register Steps** → `workspace_component_create` with `Type: pp-plugin-assembly-step`
6. **Link projects** → `dotnet add reference ../Plugins.{Domain}` from the Logic solution

Call `workspace_component_parameter_list` for required parameters at each step.

**Prerequisites:** The plugin assembly and step templates use C# file-based apps (`.cs`) for code generation — runs natively with .NET 10 SDK.

## Execution Stage Decision Tree

- **Pre-validation (10)** — input validation, reject bad data early (outside transaction)
- **Pre-operation (20)** — modify data before save, calculated fields (inside transaction)
- **Post-operation (40)** — trigger side effects, create related records (inside transaction, after DB write)

## Testing

Use FakeXrmEasy for unit testing plugins without a live Dataverse environment.

## What NOT to Do

- ❌ Don't register assembly before creating the plugin project — the build won't find it
- ❌ Don't skip `SignAssembly=true` — Dataverse rejects unsigned assemblies
- ❌ Don't use Post-operation for validation — data is already saved
- ❌ Don't use Pre-validation if you need related record data — it's outside the transaction

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
