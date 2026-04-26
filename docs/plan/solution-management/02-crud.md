# Phase 2 — Solution CRUD & Lifecycle

## Design Decision: `uninstall` vs `delete`

Dataverse uses the same `DeleteRequest` for both managed and unmanaged solutions, but the effects are completely different:

| Solution Type | What `DeleteRequest` Does | User Expectation |
|---------------|--------------------------|------------------|
| **Managed** | Removes all components from the system (destructive) | "Uninstall" |
| **Unmanaged** | Removes only the solution container; components stay | "Delete" |

We expose **two commands** with type-checking:

- **`sln uninstall <name>`** — for managed solutions only. Warns: "This will remove all components installed by this solution." Rejects unmanaged solutions with: "Solution 'X' is unmanaged. Use 'sln delete' instead."
- **`sln delete <name>`** — for unmanaged solutions only. Warns: "This removes the solution container. Components will remain in the environment." Rejects managed solutions with: "Solution 'X' is managed. Use 'sln uninstall' instead."

The existing `sln uninstall` command continues to work but should be enhanced with the type check. `sln delete` is a new command in this phase.

Depends on: [Phase 0 — Infrastructure](./00-infrastructure.md)

---

## Command 1: `txc env sln create <name>`

Create a new unmanaged solution in the target environment.

### CLI command file

**File:** `src/TALXIS.CLI.Features.Environment/Solution/SolutionCreateCliCommand.cs`

```csharp
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a new unmanaged solution in the target environment."
)]
public class SolutionCreateCliCommand : ProfiledCliCommand
```

| Parameter | Kind | Description |
|-----------|------|-------------|
| `<name>` | `[CliArgument(Name = "name", Required = true)]` | Solution unique name (must follow Dataverse naming rules: alphanumeric + underscores, publisher prefix) |
| `--display-name` | `[CliOption(Name = "--display-name", Required = true)]` | Friendly display name shown in the solution list |
| `--publisher` | `[CliOption(Name = "--publisher", Required = true)]` | Publisher unique name (NOT a GUID — resolved to ID server-side) |
| `--version` | `[CliOption(Name = "--version", Required = false)]` | Semantic version string (default: `1.0.0.0`) |
| `--description` | `[CliOption(Name = "--description", Required = false)]` | Solution description |

**`ExecuteAsync()` flow:**
1. Validate inputs (name not empty, version parseable via `Version.TryParse`)
2. Call `service.CreateAsync(Profile, options, ct)` via `TxcServices.Get<ISolutionCreateService>()`
3. Render result with `RenderSingle(outcome)` — follow the JSON/text branching pattern from `SolutionUninstallCliCommand`

### Service interface

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionCreateService.cs`

This interface is defined in Phase 0. The DTOs live in the same file:

```csharp
Task<SolutionCreateOutcome> CreateAsync(
    string? profileName,
    SolutionCreateOptions options,
    CancellationToken ct);
```

**DTOs (same file):**

```csharp
public sealed record SolutionCreateOptions(
    string UniqueName,
    string DisplayName,
    string PublisherUniqueName,
    string Version,
    string? Description);

public sealed record SolutionCreateOutcome(
    string UniqueName,
    Guid SolutionId,
    string Status,   // "Created" or "AlreadyExists"
    string Message);
```

### SDK implementation

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/SolutionCreator.cs`

Constructor: `SolutionCreator(IOrganizationServiceAsync2 service, ILogger? logger = null)` — same pattern as `SolutionUninstaller`.

**Method:** `CreateAsync(SolutionCreateOptions options, CancellationToken ct)`

**Step-by-step SDK logic:**

1. **Resolve publisher unique name → GUID:**
   ```csharp
   var query = new QueryExpression("publisher")
   {
       ColumnSet = new ColumnSet("publisherid"),
       Criteria = new FilterExpression(LogicalOperator.And)
   };
   query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, options.PublisherUniqueName);
   var result = await _service.RetrieveMultipleAsync(query, ct);
   ```
   If `result.Entities.Count == 0` → throw/return error: `"Publisher '{name}' not found."`

2. **Check if solution already exists** (idempotency):
   ```csharp
   // QueryExpression on "solution" WHERE uniquename = options.UniqueName
   ```
   If found → return `SolutionCreateOutcome` with `Status = "AlreadyExists"`, include existing solution ID. Log a warning. **Do NOT throw** — this is idempotent behavior.

3. **Create the solution entity:**
   ```csharp
   var entity = new Entity(DataverseSchema.Solution.EntityName)
   {
       ["uniquename"] = options.UniqueName,
       ["friendlyname"] = options.DisplayName,
       ["version"] = options.Version,
       ["description"] = options.Description,
       ["publisherid"] = new EntityReference("publisher", publisherId),
   };

   var request = new CreateRequest { Target = entity };
   var response = (CreateResponse)await _service.ExecuteAsync(request, ct);
   ```
   Return `SolutionCreateOutcome` with `Status = "Created"` and `response.id`.

### Error handling

| Error scenario | How to handle |
|----------------|---------------|
| Publisher not found | Return error outcome with message `"Publisher '{name}' not found in target environment. Run 'txc env sln list' to verify connection, then check publisher name."` |
| Solution already exists | Return success-like outcome with `Status = "AlreadyExists"` (idempotent — NOT an error) |
| Invalid unique name (server rejects) | Catch `FaultException`/`OrganizationServiceFault`, return error with server message. Common cause: name contains invalid characters or exceeds 256 chars. |
| Invalid version format | Validate in the CLI command before calling service: `Version.TryParse(version, out _)`. Fail with `ExitValidationError`. |

### PR scope

- `SolutionCreateCliCommand.cs` (CLI command)
- `SolutionCreator.cs` (SDK class)
- DTOs: `SolutionCreateOptions`, `SolutionCreateOutcome` in `ISolutionCreateService.cs`
- DI registration in bootstrap
- Wire the command into the DotMake command tree (parent is the existing `solution` command group)

---

## Command 2: `txc env sln publish`

> **Prerequisite:** Add `publish` to CONTRIBUTING.md's approved verb vocabulary before implementing this command.

Publish all or selective customizations. Required after modifying unmanaged components.

### CLI command file

**File:** `src/TALXIS.CLI.Features.Environment/Solution/SolutionPublishCliCommand.cs`

```csharp
[CliIdempotent]
[CliCommand(
    Name = "publish",
    Description = "Publish all or selective customizations in the target environment."
)]
public class SolutionPublishCliCommand : ProfiledCliCommand
```

| Parameter | Kind | Description |
|-----------|------|-------------|
| `--entities` | `[CliOption(Name = "--entities", Required = false)]` | Comma-separated entity logical names for selective publish. Omit for publish-all. Example: `--entities account,contact` |

**`ExecuteAsync()` flow:**
1. Parse `--entities` if provided: split by comma, trim whitespace, filter empties
2. Call `service.PublishAsync(Profile, entityNames, ct)` via `TxcServices.Get<ISolutionPublishService>()`
3. Log success/failure. Publish is a void-returning operation — output is just status confirmation.

### Service interface

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionPublishService.cs`

```csharp
Task PublishAsync(
    string? profileName,
    IReadOnlyList<string>? entityLogicalNames,
    CancellationToken ct);
```

No outcome DTO needed — publish either succeeds (returns normally) or throws.

### SDK implementation

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/SolutionPublisher.cs`

Constructor: `SolutionPublisher(IOrganizationServiceAsync2 service, ILogger? logger = null)`

**Method:** `PublishAsync(IReadOnlyList<string>? entityLogicalNames, CancellationToken ct)`

**SDK logic — two paths:**

**Path A: Publish all** (when `entityLogicalNames` is null or empty):
```csharp
var request = new PublishAllXmlRequest();
await _service.ExecuteAsync(request, ct);
```

**Path B: Selective publish** (when entities are specified):
```csharp
// Build the ParameterXml that PublishXmlRequest expects
var xml = new StringBuilder("<importexportxml>");
xml.Append("<entities>");
foreach (var entity in entityLogicalNames)
{
    xml.Append($"<entity>{SecurityElement.Escape(entity)}</entity>");
}
xml.Append("</entities>");
xml.Append("</importexportxml>");

var request = new PublishXmlRequest
{
    ParameterXml = xml.ToString()
};
await _service.ExecuteAsync(request, ct);
```

> **Important:** `PublishXmlRequest.ParameterXml` requires a specific XML schema. The `<importexportxml><entities><entity>` nesting is documented in the SDK. Invalid entity names won't cause an exception — they'll be silently ignored.

### Error handling

| Error scenario | How to handle |
|----------------|---------------|
| Publish fails (server-side) | `ExecuteAsync` will throw `FaultException`. Let it propagate — the service layer catches and logs. Common cause: dependent component errors blocking publish. |
| Invalid entity name | No pre-check needed — Dataverse silently skips unknown entities. Log a warning if desired. |
| Timeout on large environments | `PublishAllXmlRequest` can take minutes on environments with many customizations. No special handling needed — the SDK call is synchronous (server-side async). Consider logging `"Publishing customizations... this may take a few minutes."` before the call. |

> **MCP integration note:** Evaluate whether `sln publish` should be registered in `McpToolRegistry._longRunningCommandTypes` as it can take minutes on large environments.

### PR scope

- `SolutionPublishCliCommand.cs`
- `SolutionPublisher.cs`
- `ISolutionPublishService` interface (defined in Phase 0)
- DI registration
- Wire command into command tree

---

## Existing `uninstall` Enhancement — Dependency Pre-Check

The current `SolutionUninstaller.UninstallByUniqueNameAsync()` does a raw `DeleteRequest` with **no dependency pre-check**. A failed uninstall due to dependencies gives an opaque server error. This enhancement checks first and gives the user actionable information.

### Files to modify

1. **`src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/SolutionUninstaller.cs`** — add dependency check before delete
2. **`src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionUninstallService.cs`** — add new DTOs for dependency info
3. **`src/TALXIS.CLI.Features.Environment/Solution/SolutionUninstallCliCommand.cs`** — render dependency warnings

### Changes to `SolutionUninstaller.cs`

Insert a dependency check between the `FindByUniqueNameAsync` call (line 42) and the `DeleteRequest` (line 56). The new flow:

```csharp
public async Task<SolutionUninstallOutcome> UninstallByUniqueNameAsync(
    string uniqueName,
    CancellationToken ct = default)
{
    // ... existing: validate, find by unique name, handle 0/multiple matches ...

    var target = matches[0];

    // NEW: Check for blocking dependencies before attempting delete
    var dependencies = await CheckDependenciesAsync(uniqueName, ct).ConfigureAwait(false);
    if (dependencies.Count > 0)
    {
        return new SolutionUninstallOutcome(
            trimmed,
            target.Id,
            SolutionUninstallStatus.HasDependencies,    // NEW enum value
            $"Solution has {dependencies.Count} blocking dependencies.",
            dependencies);                               // NEW property
    }

    // ... existing: DeleteRequest ...
}
```

**New private method** in `SolutionUninstaller`:
```csharp
private async Task<IReadOnlyList<SolutionDependencyRecord>> CheckDependenciesAsync(
    string solutionUniqueName,
    CancellationToken ct)
{
    var request = new RetrieveDependenciesForUninstallRequest
    {
        SolutionUniqueName = solutionUniqueName
    };

    var response = (RetrieveDependenciesForUninstallResponse)
        await _service.ExecuteAsync(request, ct).ConfigureAwait(false);

    // response.EntityCollection contains dependency rows
    // Each entity has: dependentcomponentobjectid, dependentcomponenttype,
    //                   requiredcomponentobjectid, requiredcomponenttype
    return response.EntityCollection.Entities
        .Select(e => new SolutionDependencyRecord(
            DependentComponentId: e.GetAttributeValue<Guid>("dependentcomponentobjectid"),
            DependentComponentType: e.GetAttributeValue<OptionSetValue>("dependentcomponenttype")?.Value ?? 0,
            RequiredComponentId: e.GetAttributeValue<Guid>("requiredcomponentobjectid"),
            RequiredComponentType: e.GetAttributeValue<OptionSetValue>("requiredcomponenttype")?.Value ?? 0))
        .ToList();
}
```

> **SDK message:** `RetrieveDependenciesForUninstallRequest` (from `Microsoft.Crm.Sdk.Messages`). Takes `SolutionUniqueName` (string). Returns `EntityCollection` of `dependency` rows. Tested working — returned 12 dependencies for `msdyn_RichTextEditor`.

### Changes to `ISolutionUninstallService.cs`

Add new enum value and DTO:

```csharp
public enum SolutionUninstallStatus
{
    Success,
    NotFound,
    Ambiguous,
    Failed,
    HasDependencies,    // NEW
}

// NEW record
public sealed record SolutionDependencyRecord(
    Guid DependentComponentId,
    int DependentComponentType,
    Guid RequiredComponentId,
    int RequiredComponentType);

// Update SolutionUninstallOutcome to optionally carry dependencies
public sealed record SolutionUninstallOutcome(
    string SolutionName,
    Guid? SolutionId,
    SolutionUninstallStatus Status,
    string Message,
    IReadOnlyList<SolutionDependencyRecord>? BlockingDependencies = null);
```

> **Important:** Adding the `BlockingDependencies` parameter (even with a default value) to the positional record changes how all existing construction sites work. Update all existing `SolutionUninstallOutcome` instantiations in `SolutionUninstaller.cs` (approximately lines 45, 50, 62, and the success path) to use named parameters or add the trailing `null` argument. This must be done atomically in the same commit.

### Changes to `SolutionUninstallCliCommand.cs`

When `outcome.Status == SolutionUninstallStatus.HasDependencies`:

1. Render the dependency table (type + ID for each blocker)
2. Use `ComponentTypeResolver` (from Phase 0) to show friendly type names instead of raw int codes
3. Use `ComponentNameResolver` (from Phase 0) to show display names instead of raw GUIDs, if available
4. Log: `"Solution '{name}' has {count} blocking dependencies. Resolve these before uninstalling, or use 'txc env sln uninstall-check {name}' for full details."`
5. Return `ExitError`

**Note on `--yes` and the confirmation gate:** The existing `IDestructiveCommand` / `IConfirmationPrompter` pattern already gates execution behind `--yes` or interactive confirmation. The dependency pre-check is a **separate, earlier gate** — it runs before the destructive confirmation. If dependencies exist, the command fails with an informative error and the user never reaches the "are you sure?" prompt. This is intentional: we don't want users to confirm deletion of a solution that will fail anyway.

### PR scope

This can be a standalone PR or bundled with Phase 2:

- Modified: `SolutionUninstaller.cs` — add `CheckDependenciesAsync`, update `UninstallByUniqueNameAsync`
- Modified: `ISolutionUninstallService.cs` — add `HasDependencies` status, `SolutionDependencyRecord`, update `SolutionUninstallOutcome`
- Modified: `SolutionUninstallCliCommand.cs` — render dependency info in `RenderSingle`
- Depends on Phase 0: `ComponentTypeResolver` for friendly type names (can fall back to raw int codes if Phase 0 isn't done yet)
