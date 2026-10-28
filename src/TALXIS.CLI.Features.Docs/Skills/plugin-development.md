# Plugin & Custom Code Development

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). If the TALXIS MCP server is connected instead, every command is also a tool named by joining its words with `_` (`txc workspace component create` is the tool `workspace_component_create`). The tool list is generated from the CLI commands, so each tool takes the arguments and options of its command, and the CLI help is the reference on either route.

## Key Concept

Plugins are server-side .NET classes that execute custom business logic in response to Dataverse events. Scaffold them with `txc workspace component create` — this keeps everything in source control and locally reviewable before deployment.

## Plugin Project Structure

- Project name and location are defaults: `Plugins.{Domain}/` in `src/` (e.g., `Plugins.Sales`, `Plugins.Warehouse`). Precedence: what the request says, then what the repository already has (`dotnet sln list` shows the projects of the solution file — `.slnx`, or `.sln` in older repos; read each project's `ProjectType`), then this default. Never assume names; discover them.
- `.csproj` requires: `ProjectType=Plugin`, `SignAssembly=true`. The Build SDK sets the .NET Framework target (`net472` with current SDKs); don't change it, and never retarget a plugin project to modern .NET (the Dataverse plugin sandbox runs .NET Framework).
- Plugin class naming is a default too: `{Action}{Entity}Plugin.cs` (e.g., `ValidateContactPlugin.cs`)

## Registration Chain

**Order matters** — each step depends on the previous one. Examples use the default names (`src/Plugins.Warehouse`, `src/Solutions.Logic`); substitute what the repository has.

1. **Create the plugin project**:
   ```
   txc workspace component create pp-plugin --output src/Plugins.Warehouse --param PublisherName=<publisher> --param Company=<company>
   ```
   `--output` is the directory the project is created in. Run `txc workspace component parameter list pp-plugin` for the other parameters (for example `SigningKeyFilePath`).
2. **Write plugin classes** extending `PluginBase` (one `.cs` file per plugin)
3. **Reference the plugin project from the solution project that holds the registrations** (by default the Logic solution):
   ```
   dotnet add src/Solutions.Logic reference src/Plugins.Warehouse
   ```
4. **Say when each class runs** — see "Declaring steps" below.
5. **Build the solution project, not just the plugin project**: `dotnet build src/Solutions.Logic`. The build compiles the referenced plugin and registers the plugin assembly in the solution itself (`GeneratePluginAssembly` defaults to true), so there is no separate assembly step. With `[PluginStep]` attributes the same build also generates the steps.

### Declaring steps

Choose one way per plugin class and message/entity/stage. Don't mix them for the same step.

**Default: `[PluginStep]` attributes on the class.** The build generates the step from the attribute:

```csharp
using TALXIS.DevKit.Build.Dataverse.Plugin;

[PluginStep("Update", "account", PluginStage.PostOperation,
    FilteringAttributes = new[] { "name", "telephone1" })]
public class UpdateAccountPlugin : PluginBase
```

- Requires `GeneratePluginAssembly` not set to `false` (the default is true). With `false` the build generates neither the assembly data nor the steps, and `[PluginStep]` is silently ignored.
- Requires a Build SDK that ships the attributes (first shipped in 1.10.0; older SDKs do not compile `[PluginStep]`). Check the `Sdk="TALXIS.DevKit.Build.Sdk/<version>"` line of the plugin's `.csproj`, or whether `using TALXIS.DevKit.Build.Dataverse.Plugin;` compiles.
- `[PluginStepImage]` adds pre/post images. The attribute also supports async steps (PostOperation only) and messages the manual template does not list (for example Associate/Disassociate, or custom actions with `MessageId`).

**Manual: `pp-plugin-assembly-step`.** Use it only for what attributes cannot express (Power Fx step types, impersonating a specific user, secure configuration) and for projects on a Build SDK without `[PluginStep]`. Never for a class that already declares `[PluginStep]` for the same message, entity and stage. It needs the solution's `PluginAssemblies/*.dll.data.xml`, so **build the solution once first (step 5), then add steps**:

```
txc workspace component create pp-plugin-assembly-step --output src/Solutions.Logic --param PrimaryEntity=account --param PluginProjectName=Plugins.Warehouse --param PluginName=UpdateAccountPlugin --param Stage=Post-operation --param SdkMessage=Update
```

- `Stage` is `Pre-validation` (default), `Pre-operation` or `Post-operation`; `SdkMessage` defaults to `Create`. The step is synchronous and has no images. Add `--param SolutionRootPath=<folder>` when the unpacked solution files are in a subfolder of the project (default `.`).
- The step names the class as `<PluginProjectName>.<PluginName>`, so the class's namespace must match `PluginProjectName`. If the `.csproj` sets a custom `<AssemblyName>`, also pass `--param AssemblyName=<name>`.
- If a hand-written step matches a `[PluginStep]` class (same class, message, entity and stage), the build prints a warning ("would run twice"), keeps both and they run twice. Delete the hand-written XML, or set `Id` on the attribute to the existing step's id to adopt it (the build then rewrites that step file from the attribute).

**Manual assembly registration: `pp-plugin-assembly`.** Only for a solution project that sets `GeneratePluginAssembly=false` (then `[PluginStep]` does not work either; steps must be manual). It reads the compiled output, so **build and sign the plugin project first** (`dotnet build src/Plugins.Warehouse`), then:

```
txc workspace component create pp-plugin-assembly --output src/Solutions.Logic --param AssemblyId=<new-guid> --param PluginProjectRootPath=src/Plugins.Warehouse
```

Run it once per assembly: every run adds another assembly root component. After the build has already registered the assembly (the default), a second root breaks the next pack with "root components are not defined in customizations". Then add steps with `pp-plugin-assembly-step` as above.

Run `txc workspace component parameter list <template>` for the parameters of each template.

**Prerequisites:** the templates run post-action scripts, some of them C# file-based apps (`.cs`) that run natively with the .NET 10 SDK.

## Execution Stage Decision Tree

- **Pre-validation (10)** — input validation, reject bad data early (outside transaction)
- **Pre-operation (20)** — modify data before save, calculated fields (inside transaction)
- **Post-operation (40)** — trigger side effects, create related records (inside transaction, after DB write)

## Testing

Use FakeXrmEasy for unit testing plugins without a live Dataverse environment.

## What NOT to Do

- ❌ Don't run `pp-plugin-assembly` after the build has registered the assembly, and don't run it in a solution that uses `[PluginStep]` — it adds a second assembly root and the next pack fails
- ❌ Don't register a step with `pp-plugin-assembly-step` for a class that already has a matching `[PluginStep]` — it would run twice
- ❌ Don't set `GeneratePluginAssembly=false` and expect `[PluginStep]` to work — it is silently ignored
- ❌ Don't build only the plugin project and expect registration — build the solution that references it
- ❌ Don't skip `SignAssembly=true` — Dataverse rejects unsigned assemblies
- ❌ Don't use Post-operation for validation — data is already saved
- ❌ Don't use Pre-validation if you need related record data — it's outside the transaction

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
