# Phase 3 — Component & Layer Mutations

Depends on: [Phase 1 — Inspection](./01-inspection.md) (for `ComponentTypeResolver` and `ComponentNameResolver`)

---

## Command 1: `txc env sln component add <solution>`

Add an existing component to an unmanaged solution. This is the SDK equivalent of "Add existing → [component]" in the solution explorer UI.

### CLI command file

**File:** `src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentAddCliCommand.cs`

```csharp
[CliIdempotent]
[CliCommand(
    Name = "add",
    Description = "Add an existing component to an unmanaged solution."
)]
public class SolutionComponentAddCliCommand : ProfiledCliCommand
```

| Parameter | Kind | Description |
|-----------|------|-------------|
| `<solution>` | `[CliArgument(Name = "solution", Required = true)]` | Target solution unique name (must be unmanaged) |
| `--component-id` | `[CliOption(Name = "--component-id", Required = true)]` | Component GUID (the `objectid` of the component to add) |
| `--type` | `[CliOption(Name = "--type", Required = true)]` | Component type — accepts integer code or friendly name (e.g., `Entity`, `1`, `WebResource`, `61`). Resolved via `ComponentTypeResolver`. |
| `--add-required` | `[CliOption(Name = "--add-required", Required = false)]` | When set, also add components that the target depends on (`AddRequiredComponents = true`). Default: false. |
| `--exclude-subcomponents` | `[CliOption(Name = "--exclude-subcomponents", Required = false)]` | When set, do not include subcomponents (`DoNotIncludeSubcomponents = true`). Controls `RootComponentBehavior`. Default: false (include subcomponents). |

**`ExecuteAsync()` flow:**
1. Resolve `--type` string → int type code via `ComponentTypeResolver.Resolve(typeString)`. If unresolvable, return `ExitValidationError` with a message listing valid type names.
2. Parse `--component-id` as `Guid`. If invalid, return `ExitValidationError`.
3. Call `service.AddAsync(Profile, options, ct)` via `TxcServices.Get<ISolutionComponentMutationService>()`
4. Render the result (component ID, solution name, status).

### Service interface

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionComponentMutationService.cs`

```csharp
Task<ComponentAddOutcome> AddAsync(
    string? profileName,
    ComponentAddOptions options,
    CancellationToken ct);
```

**DTOs:**

```csharp
public sealed record ComponentAddOptions(
    string SolutionUniqueName,
    Guid ComponentId,
    int ComponentType,
    bool AddRequiredComponents,
    bool DoNotIncludeSubcomponents);

public sealed record ComponentAddOutcome(
    Guid ComponentId,
    int ComponentType,
    string SolutionUniqueName,
    string Status,      // "Added" or "AlreadyPresent"
    string Message);
```

### SDK implementation

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/SolutionComponentManager.cs`

This class handles both `add` and `remove` operations (they share the same constructor and service dependency). Constructor: `SolutionComponentManager(IOrganizationServiceAsync2 service, ILogger? logger = null)`

**Method:** `AddAsync(ComponentAddOptions options, CancellationToken ct)`

**SDK logic:**

```csharp
var request = new AddSolutionComponentRequest
{
    SolutionUniqueName = options.SolutionUniqueName,
    ComponentId = options.ComponentId,
    ComponentType = options.ComponentType,
    AddRequiredComponents = options.AddRequiredComponents,
    DoNotIncludeSubcomponents = options.DoNotIncludeSubcomponents,
};

var response = (AddSolutionComponentResponse)
    await _service.ExecuteAsync(request, ct).ConfigureAwait(false);

// response.id is the solutioncomponentid of the newly created link row
return new ComponentAddOutcome(
    options.ComponentId,
    options.ComponentType,
    options.SolutionUniqueName,
    Status: "Added",
    Message: $"Component added (solutioncomponentid: {response.id}).");
```

> **SDK message:** `AddSolutionComponentRequest` from `Microsoft.Crm.Sdk.Messages`. Key properties: `SolutionUniqueName` (string, not GUID), `ComponentId` (Guid), `ComponentType` (int), `AddRequiredComponents` (bool), `DoNotIncludeSubcomponents` (bool).

**Idempotency:** Adding a component that's already in the solution does **not** throw — the SDK silently succeeds and returns the existing `solutioncomponentid`. No pre-check needed. To detect "already present" vs "newly added", optionally query `solutioncomponent` before the add, but this is a nice-to-have — returning `"Added"` in both cases is acceptable for v1.

### Error handling

| Error scenario | How to handle |
|----------------|---------------|
| Solution not found | `FaultException` with `ObjectDoesNotExist` code. Catch and return error: `"Solution '{name}' not found."` |
| Solution is managed | `FaultException` — cannot add components to managed solutions. Return error: `"Cannot add components to managed solution '{name}'."` |
| Component not found | `FaultException` — the component GUID doesn't exist. Return error: `"Component {id} (type {type}) not found in the environment."` |
| Invalid component type code | `ComponentTypeResolver` fails before the SDK call — handle in CLI command. |
| Circular dependency | Extremely rare. Let the server error propagate with its message. |

### PR scope

- `Solution/Component/SolutionComponentAddCliCommand.cs`
- `SolutionComponentManager.cs` (new file — shared by add and remove)
- DTOs: `ComponentAddOptions`, `ComponentAddOutcome` in `ISolutionComponentMutationService.cs`
- DI registration
- Wire command into command tree (parent: `component` subcommand under `solution`)

---

## Command 2: `txc env sln component remove <solution>`

Remove a component from an unmanaged solution. This removes the association — it does **NOT** delete the component from the environment. Think of it as "remove from this suitcase, leave in the environment."

### CLI command file

**File:** `src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentRemoveCliCommand.cs`

```csharp
[CliCommand(
    Name = "remove",
    Description = "Remove a component from an unmanaged solution (does not delete the component from the environment)."
)]
[CliDestructive("Removes the component from the solution. The component remains in the environment but is no longer tracked by this solution.")]
public class SolutionComponentRemoveCliCommand : ProfiledCliCommand, IDestructiveCommand
```

| Parameter | Kind | Description |
|-----------|------|-------------|
| `<solution>` | `[CliArgument(Name = "solution", Required = true)]` | Solution unique name |
| `--component-id` | `[CliOption(Name = "--component-id", Required = true)]` | Component GUID to remove |
| `--type` | `[CliOption(Name = "--type", Required = true)]` | Component type (integer or friendly name) |
| `--yes` | `[CliOption(Name = "--yes", Required = false)]` | Skip confirmation prompt (required by `IDestructiveCommand`) |

**Why `[CliDestructive]`:** Removing a component from a solution can cause it to become untracked. If the component only existed in this solution, it effectively becomes part of the "Default" solution with no transport path. This is not easily reversible (you'd have to re-add it manually with the correct `RootComponentBehavior`).

**`ExecuteAsync()` flow:**
1. Resolve `--type` → int via `ComponentTypeResolver`
2. Parse `--component-id` as `Guid`
3. Call `service.RemoveAsync(Profile, options, ct)` via `TxcServices.Get<ISolutionComponentMutationService>()`
4. Render result

### Service interface

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionComponentMutationService.cs`

```csharp
Task<ComponentRemoveOutcome> RemoveAsync(
    string? profileName,
    ComponentRemoveOptions options,
    CancellationToken ct);
```

**DTOs:**

```csharp
public sealed record ComponentRemoveOptions(
    string SolutionUniqueName,
    Guid ComponentId,
    int ComponentType);

public sealed record ComponentRemoveOutcome(
    Guid ComponentId,
    int ComponentType,
    string SolutionUniqueName,
    string Status,      // "Removed" or "NotFound"
    string Message);
```

### SDK implementation

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/SolutionComponentManager.cs` (same file as `add`)

**Method:** `RemoveAsync(ComponentRemoveOptions options, CancellationToken ct)`

**SDK logic:**

```csharp
var request = new RemoveSolutionComponentRequest
{
    SolutionUniqueName = options.SolutionUniqueName,
    ComponentId = options.ComponentId,
    ComponentType = options.ComponentType,
};

await _service.ExecuteAsync(request, ct).ConfigureAwait(false);

return new ComponentRemoveOutcome(
    options.ComponentId,
    options.ComponentType,
    options.SolutionUniqueName,
    Status: "Removed",
    Message: "Component removed from solution.");
```

> **SDK message:** `RemoveSolutionComponentRequest` from `Microsoft.Crm.Sdk.Messages`. Takes `SolutionUniqueName` (string), `ComponentId` (Guid), `ComponentType` (int). Void response — success means the link row was deleted.

### Error handling

| Error scenario | How to handle |
|----------------|---------------|
| Component not in this solution | `FaultException`. Catch and return `Status: "NotFound"`: `"Component {id} is not in solution '{name}'."` Return `ExitSuccess` (idempotent remove semantics — if it's not there, that's fine). |
| Solution is managed | `FaultException`. Return error: `"Cannot remove components from managed solution '{name}'."` |
| Component is a subcomponent | Dataverse may prevent removal of subcomponents (e.g., attributes added as part of an entity root component). Return the server error message. |

### Pre-checks

None required for v1. The SDK call fails fast with a clear error. Optional future enhancement: query `solutioncomponent` first to verify the component exists in the solution, and show a more user-friendly message.

### PR scope

- `Solution/Component/SolutionComponentRemoveCliCommand.cs`
- Add `RemoveAsync` method to `SolutionComponentManager.cs`
- DTOs: `ComponentRemoveOptions`, `ComponentRemoveOutcome` in `ISolutionComponentMutationService.cs`
- DI registration
- Wire command into command tree

**Recommended:** Bundle `add` and `remove` in the same PR since they share `SolutionComponentManager.cs` and `ISolutionComponentMutationService.cs`.

---

## Command 3: `txc env sln component layer remove-customization <component-id>`

Remove the unmanaged active layer from a component, reverting it to the behavior defined by the highest managed layer. This is a **component-level** operation — layers are a property of a component, not of a solution.

This is the CLI equivalent of the "Remove active customization" button in the solution layers UI.

### CLI command file

**File:** `src/TALXIS.CLI.Features.Environment/Component/Layer/ComponentLayerRemoveCustomizationCliCommand.cs`

> **Note on path:** This goes under `Component/Layer/`, not `Solution/`, because it's a component-scoped layer command (`txc env comp layer remove-customization`), matching Phase 1's structure where `ComponentLayerListCliCommand` and `ComponentLayerShowCliCommand` live in `Component/Layer/`.

```csharp
[CliCommand(
    Name = "remove-customization",
    Description = "Remove the unmanaged active layer from a component, reverting to the highest managed layer."
)]
[CliDestructive("Permanently removes unmanaged customizations from the component. This cannot be undone.")]
public class ComponentLayerRemoveCustomizationCliCommand : ProfiledCliCommand, IDestructiveCommand
```

| Parameter | Kind | Description |
|-----------|------|-------------|
| `<component-id>` | `[CliArgument(Name = "component-id", Required = true)]` | Component GUID (the `objectid`, not the `solutioncomponentid`) |
| `--type` | `[CliOption(Name = "--type", Required = true)]` | Component type (integer or friendly name). Required because the layer system is keyed by `(objectid, componenttype)`. |
| `--yes` | `[CliOption(Name = "--yes", Required = false)]` | Skip confirmation (required by `IDestructiveCommand`) |

**`ExecuteAsync()` flow:**
1. Resolve `--type` → int via `ComponentTypeResolver`
2. Parse `<component-id>` as `Guid`
3. Call `service.RemoveCustomizationAsync(Profile, componentId, componentType, ct)` via `TxcServices.Get<ISolutionLayerMutationService>()`
4. Render result — show which layers remain after removal

### Service interface

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionLayerMutationService.cs`

```csharp
Task<RemoveCustomizationOutcome> RemoveCustomizationAsync(
    string? profileName,
    Guid componentId,
    int componentType,
    CancellationToken ct);
```

**DTOs:**

```csharp
public sealed record RemoveCustomizationOutcome(
    Guid ComponentId,
    int ComponentType,
    string Status,      // "Removed", "NoActiveLayer", "OnlyActiveLayer"
    string Message);
```

### SDK implementation

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/ComponentLayerManager.cs`

Constructor: `ComponentLayerManager(IOrganizationServiceAsync2 service, ILogger? logger = null)`

**Method:** `RemoveCustomizationAsync(Guid componentId, int componentType, CancellationToken ct)`

**Step-by-step SDK logic with pre-checks:**

**Pre-check 1 — Verify active layer exists:**

```csharp
// Query msdyn_componentlayer via Web API (virtual entity — not queryable via SDK QueryExpression)
// URL: msdyn_componentlayers?$filter=(msdyn_componentid eq '{componentId}'
//       and msdyn_solutioncomponentname eq '{componentTypeName}')
//      &$select=msdyn_solutionname,msdyn_order,msdyn_name
//      &$orderby=msdyn_order desc

// Use ServiceClient.ExecuteWebRequest() — same pattern as Phase 1 layer list command
var layers = await QueryComponentLayersAsync(componentId, componentTypeName, ct);
```

Check the returned layers:

```csharp
var activeLayer = layers.FirstOrDefault(l => l.SolutionName == "Active");

if (activeLayer == null)
{
    return new RemoveCustomizationOutcome(
        componentId, componentType,
        Status: "NoActiveLayer",
        Message: "No active (unmanaged) layer found for this component. Nothing to remove.");
}
```

**Pre-check 2 — Verify active layer is NOT the only layer:**

```csharp
if (layers.Count == 1)
{
    // Only the Active layer exists — no managed layer beneath it.
    // RemoveActiveCustomizationsRequest would delete the component entirely,
    // which is almost certainly not what the user wants.
    return new RemoveCustomizationOutcome(
        componentId, componentType,
        Status: "OnlyActiveLayer",
        Message: "The active layer is the only layer for this component (no managed layers beneath). "
               + "Removing it would delete the component. Use a direct delete operation instead.");
}
```

**Execute the removal:**

```csharp
var request = new RemoveActiveCustomizationsRequest
{
    SolutionComponentName = componentTypeName,  // e.g., "entity", "systemform"
    ComponentId = componentId,
};

await _service.ExecuteAsync(request, ct).ConfigureAwait(false);

return new RemoveCustomizationOutcome(
    componentId, componentType,
    Status: "Removed",
    Message: $"Active customizations removed. Component now reflects the highest managed layer.");
```

> **SDK message:** `RemoveActiveCustomizationsRequest` from `Microsoft.Crm.Sdk.Messages`. Note the **plural** form — `Customizations`, not `Customization`. Key properties:
> - `SolutionComponentName` (string) — the **logical name** of the component type (e.g., `"entity"`, `"systemform"`, `"workflow"`), NOT the friendly name or int code. Use `ComponentTypeResolver` to map int code → schema name.
> - `ComponentId` (Guid) — the object ID of the component.
>
> The request is **synchronous** — it either succeeds or throws. No async polling needed.

### Error handling

| Error scenario | How to handle |
|----------------|---------------|
| No active layer exists | Caught by pre-check 1 → return `"NoActiveLayer"` (not an error, just informational). |
| Active layer is the only layer | Caught by pre-check 2 → return `"OnlyActiveLayer"` with explanation. Exit with error code. |
| Component doesn't exist | Web API query returns empty results in pre-check → return error: `"Component {id} (type {type}) not found or has no layers."` |
| Server rejects removal | `FaultException`. Some components cannot have their active layer removed (e.g., components with managed properties blocking it). Return the server error message. |
| Component type name mismatch | `RemoveActiveCustomizationsRequest.SolutionComponentName` must use the **schema name** (e.g., `"entity"` not `"Entity"` or `"Table"`). `ComponentTypeResolver` must provide the correct SDK schema name for each type. |

### Pre-checks summary

These pre-checks are **mandatory** — they prevent data loss and confusing errors:

| # | Check | How | Failure behavior |
|---|-------|-----|------------------|
| 1 | Active layer exists | Web API query `msdyn_componentlayers` filtered by component ID + type, look for `msdyn_solutionname eq 'Active'` | Return `"NoActiveLayer"` — nothing to do |
| 2 | Active layer is not the only layer | `layers.Count > 1` | Return `"OnlyActiveLayer"` — removing it would delete the component entirely |
| 3 | User confirmation | `IDestructiveCommand` + `IConfirmationPrompter` (handled by base class) | Block execution unless `--yes` is passed or user confirms interactively |

### PR scope

- `Component/Layer/ComponentLayerRemoveCustomizationCliCommand.cs`
- `ComponentLayerManager.cs` (new file)
- DTOs: `RemoveCustomizationOutcome` in `ISolutionLayerMutationService.cs`
- DI registration
- Wire command into command tree (parent: `layer` subcommand under `component`)
- Depends on Phase 0: `ComponentTypeResolver` for resolving type strings → int codes AND int codes → schema names
