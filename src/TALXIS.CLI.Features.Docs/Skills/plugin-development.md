# Plugin & Custom Code Development

## Key Concept

Plugins are server-side .NET classes that execute custom business logic in response to Dataverse events. All plugin scaffolding uses `txc` workspace tools — this keeps everything in source control and locally reviewable before deployment.

## Plugin Project Structure

- Convention: `Plugins.{Domain}/` in `src/` (e.g., `Plugins.Sales`, `Plugins.Warehouse`)
- `.csproj` requires: `ProjectType=Plugin`, `SignAssembly=true`; the Build SDK targets `net472` (the template sets no `TargetFramework`)
- Plugin classes follow `{Action}{Entity}Plugin.cs` naming (e.g., `ValidateContactPlugin.cs`)

## Registration Chain

**Order matters** — each step depends on the previous one.

1. **Create Plugin Project** → `txc workspace component create pp-plugin`
2. **Write plugin classes** extending `PluginBase` (one `.cs` file per plugin)
3. **Link projects** → `dotnet add reference ../Plugins.{Domain}` from the Logic solution
4. **Build** → `dotnet build` the Logic solution. The build registers the plugin assembly in the solution itself (`GeneratePluginAssembly` defaults to true), so there is no separate assembly step.
5. **Register Steps** → `txc workspace component create pp-plugin-assembly-step`

Run `txc workspace component parameter list <template>` for the required parameters at each step.

`pp-plugin-assembly` is only for solutions that set `GeneratePluginAssembly=false`. Don't run it after the build has registered the assembly: it adds a second assembly root, and the next build fails with "root components are not defined in customizations".

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
