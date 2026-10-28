# Plugin & Custom Code Development

Plugins are server-side .NET classes that run business logic on Dataverse events.

## Plugin Project

- Default: `src/Plugins.{Domain}/`, classes `{Action}{Entity}Plugin.cs`.
- `.csproj` needs `ProjectType=Plugin` and `SignAssembly=true`. Target is .NET Framework (Build SDK default `net472`); keep the project's.

## Registration Chain

1. `txc workspace component create pp-plugin --output src/Plugins.Warehouse --param PublisherName=<publisher> --param Company=<company>`
2. Write classes extending `PluginBase`.
3. Reference the project from the solution project that holds the registrations: `dotnet add src/Solutions.Logic reference src/Plugins.Warehouse/Plugins.Warehouse.csproj`
4. Declare steps (below).
5. Build the solution project, not just the plugin: `dotnet build src/Solutions.Logic`. Current Build SDKs register the assembly there (`GeneratePluginAssembly` defaults to true) and generate `[PluginStep]` steps.

### Declaring steps

One way per class, message, entity and stage; if the repository's plugins register steps another way, follow it.

**Default: `[PluginStep]` on the class** (`using TALXIS.DevKit.Build.Dataverse.Plugin;`):

```csharp
[PluginStep("Update", "account", PluginStage.PostOperation,
    FilteringAttributes = new[] { "name", "telephone1" })]
```

- Needs `GeneratePluginAssembly` not `false` (otherwise the attribute is silently ignored) and Build SDK >= 1.10.0 in **both** the plugin and the solution project (`Sdk="TALXIS.DevKit.Build.Sdk/<version>"`). An older solution project builds but generates no steps.
- `[PluginStepImage]` adds pre/post images.
- `FilteringAttributes` (Update): the step runs when one of them is present in the request, not only when changed. To react to a real change, compare `Target` with a pre-image.
- A hand-written step for the same class, message, entity and stage makes the build warn "would run twice" and keep both. If it is already deployed, set `Id` on the attribute to that step's id (the XML under `SdkMessageProcessingSteps/`) to update it in place.

**Manual `pp-plugin-assembly-step`:** only for what attributes cannot express (Power Fx step types, impersonating a user; edit the generated XML) or Build SDKs without `[PluginStep]`; never for a class that already declares the step. Build the solution once first, then `txc workspace component create pp-plugin-assembly-step --output src/Solutions.Logic --param PluginProjectName=Plugins.Warehouse --param PluginName=UpdateAccountPlugin ...`. The class is `<PluginProjectName>.<PluginName>`, so its namespace must match; with a custom `<AssemblyName>`, also pass `AssemblyName`.

**Manual `pp-plugin-assembly`:** only with `GeneratePluginAssembly=false` or an older Build SDK. Build and sign the plugin project first (it reads the compiled output), then `txc workspace component create pp-plugin-assembly --output src/Solutions.Logic --param AssemblyId=<new-guid> --param PluginProjectRootPath=../Plugins.Warehouse` (relative to `--output`). Run once per assembly: a second root breaks the next pack.

## Execution Stages

- **Pre-validation (10)**: reject bad input, outside the transaction.
- **Pre-operation (20)**: modify data before save.
- **Post-operation (40)**: side effects, related records; data is already saved, so not for validation.

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
