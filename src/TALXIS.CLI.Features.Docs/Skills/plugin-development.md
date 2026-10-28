# Plugin & Custom Code Development

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## Key Concept

Plugins are server-side .NET classes that execute custom business logic in response to Dataverse events. Scaffold them with `txc workspace component create` — this keeps everything in source control and locally reviewable before deployment.

## Plugin Project Structure

- Project name and location are defaults: `Plugins.{Domain}/` in `src/` (e.g., `Plugins.Sales`, `Plugins.Warehouse`). Precedence: what the request says, then what the repository already has (`dotnet sln list` shows the projects of the solution file — `.slnx`, or `.sln` in older repos; read each project's `ProjectType`), then this default. Never assume names; discover them.
- `.csproj` requires: `ProjectType=Plugin`, `SignAssembly=true`. Dataverse plugin assemblies target .NET Framework. The Build SDK sets the target (`net472` by default); keep the project's existing target.
- Plugin class naming is a default too: `{Action}{Entity}Plugin.cs` (e.g., `ValidateContactPlugin.cs`)

## Registration Chain

**Order matters** — each step depends on the previous one. Examples use the default names (`src/Plugins.Warehouse`, `src/Solutions.Logic`); substitute what the repository has.

1. **Create the plugin project**:
   ```
   txc workspace component create pp-plugin --output src/Plugins.Warehouse --param PublisherName=<publisher> --param Company=<company>
   ```
   `--output` is the folder the component is scaffolded into (for `pp-plugin`: the project folder). Run `txc workspace component parameter list pp-plugin` for the other parameters (for example `SigningKeyFilePath`).
2. **Write plugin classes** extending `PluginBase` (one `.cs` file per plugin)
3. **Reference the plugin project from the solution project that holds the registrations** (by default the Logic solution):
   ```
   dotnet add src/Solutions.Logic reference src/Plugins.Warehouse/Plugins.Warehouse.csproj
   ```
4. **Say when each class runs** — see "Declaring steps" below.
5. **Build the solution project, not just the plugin project**: `dotnet build src/Solutions.Logic`. The build compiles the referenced plugin and, with current Build SDKs, registers the plugin assembly in the solution itself (`GeneratePluginAssembly` defaults to true), so there is no separate assembly step. With `[PluginStep]` attributes the same build also generates the steps. An older Build SDK, or `GeneratePluginAssembly=false`, needs the manual route below.

### Declaring steps

Choose one way per plugin class and message/entity/stage. Don't mix them for the same step. If the repository's existing plugins already register their steps another way (hand-written step XML, `pp-plugin-assembly-step`), follow that unless the user asks to switch.

**Default for new work: `[PluginStep]` attributes on the class.** The build generates the step from the attribute:

```csharp
using TALXIS.DevKit.Build.Dataverse.Plugin;

[PluginStep("Update", "account", PluginStage.PostOperation,
    FilteringAttributes = new[] { "name", "telephone1" })]
public class UpdateAccountPlugin : PluginBase
```

- Requires `GeneratePluginAssembly` not set to `false` (the default is true). With `false` the build generates neither the assembly data nor the steps, and `[PluginStep]` is silently ignored.
- Requires a Build SDK that ships the attributes (first shipped in 1.10.0) in **both** the plugin project and the solution project, because the solution project's build generates the steps. Check the `Sdk="TALXIS.DevKit.Build.Sdk/<version>"` line of both `.csproj` files, or whether `using TALXIS.DevKit.Build.Dataverse.Plugin;` compiles in the plugin. An older solution project builds fine but generates no steps.
- `[PluginStepImage]` adds pre/post images. The attribute also supports async steps (PostOperation only) and messages the manual template does not list (for example Associate/Disassociate, or custom actions with `MessageId`).
- `FilteringAttributes` (Update only): the step runs when one of those attributes is present in the request, not only when its value changed. To react to a real change, compare `Target` with a pre-image (`[PluginStepImage]`).
- If a hand-written step for the same class, message, entity and stage already exists, the build warns ("would run twice") and keeps both. Rule of thumb: if that step is already deployed, set `Id` on the attribute to the existing step's id (the file name, and the `Id` inside it, of the XML under `SdkMessageProcessingSteps/` in the solution project); the build then rewrites that file from the attribute and the same step record is updated in place. Deleting the XML and generating a new step gives it a different id.

**Manual: `pp-plugin-assembly-step`.** Use it for projects on a Build SDK without `[PluginStep]`, and for what the attribute cannot express (for example Power Fx step types, impersonating a specific user; then edit the generated XML). Never for a class that already declares `[PluginStep]` for the same message, entity and stage. It needs the solution's `PluginAssemblies/*.dll.data.xml`, so **build the solution once first (step 5), then add steps**:

```
txc workspace component create pp-plugin-assembly-step --output src/Solutions.Logic --param PrimaryEntity=account --param PluginProjectName=Plugins.Warehouse --param PluginName=UpdateAccountPlugin --param Stage=Post-operation --param SdkMessage=Update
```

`txc workspace component parameter list pp-plugin-assembly-step` shows the parameters and defaults. The step names the class as `<PluginProjectName>.<PluginName>`, so the class's namespace must match `PluginProjectName`; if the `.csproj` sets a custom `<AssemblyName>`, also pass `--param AssemblyName=<name>`.

**Manual assembly registration: `pp-plugin-assembly`.** Only for a solution project that sets `GeneratePluginAssembly=false` (then `[PluginStep]` does not work either; steps must be manual) or an older Build SDK that does not register the assembly. It reads the compiled output, so **build and sign the plugin project first** (`dotnet build src/Plugins.Warehouse`), then:

```
txc workspace component create pp-plugin-assembly --output src/Solutions.Logic --param AssemblyId=<new-guid> --param PluginProjectRootPath=../Plugins.Warehouse
```

`PluginProjectRootPath` is relative to `--output` (or absolute). Run it once per assembly: every run adds another assembly root component. If the build has already registered the assembly, the second root is not defined in the solution and the next pack/build fails. Then add steps with `pp-plugin-assembly-step` as above.

**Prerequisites:** the templates run post-action scripts, some of them C# file-based apps (`.cs`) that run natively with the .NET 10 SDK.

## Execution Stage Decision Tree

- **Pre-validation (10)** — input validation, reject bad data early (outside transaction)
- **Pre-operation (20)** — modify data before save, calculated fields (inside transaction)
- **Post-operation (40)** — trigger side effects, create related records (inside transaction, after DB write)

## Testing

Use FakeXrmEasy for unit testing plugins without a live Dataverse environment.

## What NOT to Do

- ❌ Don't run `pp-plugin-assembly` when the build already registers the assembly, or in a solution that uses `[PluginStep]` — see the manual route above
- ❌ Don't register a step with `pp-plugin-assembly-step` for a class that already has a matching `[PluginStep]` — it would run twice
- ❌ Don't build only the plugin project and expect registration — build the solution that references it
- ❌ Don't skip `SignAssembly=true` — Dataverse rejects unsigned assemblies
- ❌ Don't use Post-operation for validation — data is already saved
- ❌ Don't use Pre-validation if you need related record data — it's outside the transaction

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
