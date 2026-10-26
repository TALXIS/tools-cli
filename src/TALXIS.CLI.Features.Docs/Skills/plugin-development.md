# Plugin & Custom Code Development

## Key Concept

Plugins are server-side .NET classes that execute custom business logic in response to Dataverse events. All plugin scaffolding uses `txc` workspace tools — this keeps everything in source control and locally reviewable before deployment.

## Plugin Project Structure

- Convention: `Plugins.{Domain}/` in `src/` (e.g., `Plugins.Sales`, `Plugins.Warehouse`)
- `.csproj` requires: `ProjectType=Plugin`, `SignAssembly=true`, target `net462`
- Plugin classes follow `{Action}{Entity}Plugin.cs` naming (e.g., `ValidateContactPlugin.cs`)

## Registration

The plugin project holds the code; a Dataverse solution holds the registration. By default (`GeneratePluginAssembly` is `true` in `pp-solution`) **the solution's build registers the assembly itself** — there is no template to run for it — and, when the project's build SDK supports attributes, generates the plugin steps from `[PluginStep]` on the classes. **Order matters.**

1. **Create Plugin Project** → `workspace_component_create` with `Type: pp-plugin`
2. **Write plugin classes** extending `PluginBase` (one `.cs` file per plugin) and declare when each runs (see below)
3. **Link projects** → `dotnet add reference ../Plugins.{Domain}` from the Logic solution
4. **Build the solution** → `dotnet build src/Solutions.Logic` — writes `PluginAssemblies/*.dll.data.xml`, `SdkMessageProcessingSteps/{id}.xml` and the root components into the solution source. Commit them. A second build must change no file.

### Declaring steps with attributes

```csharp
using TALXIS.DevKit.Build.Dataverse.Plugin;

[PluginStep("Update", "account", PluginStage.PostOperation, FilteringAttributes = new[] { "name" })]
[PluginStepImage(PluginImageType.PreImage, "PreImage", "name")]
public class UpdateAccountPlugin : PluginBase
```

- Built-in messages: Create, Update, Delete, SetState, SetStateDynamicEntity, Assign, Retrieve, RetrieveMultiple, Associate, Disassociate, RetrieveExchangeRate. Messages without a primary entity use `[PluginStep("Associate", PluginStage.PreOperation)]`.
- Any other message (a custom action) needs `MessageId = "<guid>"`, and that message must exist in the target or travel in the same solution. A Custom API's message id is generated when it is created, so it cannot be hard-coded.
- Optional: `Mode = PluginMode.Asynchronous` (Post-operation only), `AsyncAutoDelete`, `Rank`, `Configuration`, `RunAsSystem`, `Name`, `Description`. `InvocationSource` follows what Dataverse stores for the message; set it only to deviate.
- To adopt an existing step, set `Id` to its id (the file name in `SdkMessageProcessingSteps/`), otherwise the build warns that both would run. Removing the attribute removes the generated step; hand-written steps are never touched.
- A Custom API's backing plugin is bound on the Custom API definition, not with `[PluginStep]`.
- The build fails, naming the class, on invalid registrations (pre-image on Create, post-image outside Post-operation, asynchronous outside Post-operation, ambiguous image binding, unknown message without `MessageId`, duplicate ids).

### Manual registration

Only for what attributes cannot express (Power Fx step types, impersonating a specific user, secure configuration), or when `using TALXIS.DevKit.Build.Dataverse.Plugin;` does not compile because the project's build SDK predates attribute registration:

- **Steps** → `workspace_component_create` with `Type: pp-plugin-assembly-step`, after the assembly registration exists (after the first build). Never for a class that already declares the same step with `[PluginStep]`.
- **Assembly** → `pp-plugin-assembly` only when the solution sets `GeneratePluginAssembly=false`, or once before its first build to fix the assembly id (the plugin project must be built first; it reads the compiled DLL). **Never after a build has registered the assembly** — it adds a second type-91 root component and the next build fails with "root components are not defined in customizations". `[PluginStep]` works only with `GeneratePluginAssembly=true`.

Call `workspace_component_parameter_list` for required parameters of each template.

**Prerequisites:** The plugin assembly and step templates use C# file-based apps (`.cs`) for code generation — runs natively with .NET 10 SDK.

## Execution Stage Decision Tree

- **Pre-validation (10)** — input validation, reject bad data early (outside transaction)
- **Pre-operation (20)** — modify data before save, calculated fields (inside transaction)
- **Post-operation (40)** — trigger side effects, create related records (inside transaction, after DB write)

## Testing

Use FakeXrmEasy for unit testing plugins without a live Dataverse environment.

## What NOT to Do

- ❌ Don't run `pp-plugin-assembly` after a build has registered the assembly, or in a solution that uses `[PluginStep]` — it duplicates the registration and breaks the next build
- ❌ Don't skip `SignAssembly=true` — Dataverse rejects unsigned assemblies
- ❌ Don't use Post-operation for validation — data is already saved
- ❌ Don't use Pre-validation if you need related record data — it's outside the transaction

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
