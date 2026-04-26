# Plan vs. Codebase Audit Report

**Audited:** `docs/plan/solution-management/` (README + Phases 0–4)
**Against:** actual source in `src/`
**Date:** 2025-07-22

---

## 🔴 Breaking — Will cause compile errors or runtime failures

### B1. `OutputFormatter.WriteRaw` signature mismatch

**Plan (01-inspection.md, line ~1172):**
```csharp
OutputFormatter.WriteRaw(json, raw => {
    OutputWriter.WriteLine(PrettyPrint(raw));
});
```
The plan passes `Action<string>` (a callback receiving the raw string).

**Actual (`OutputFormatter.cs`, line 51):**
```csharp
public static void WriteRaw(string json, Action? textRenderer = null)
```
`WriteRaw` takes `Action` (parameterless), NOT `Action<string>`. The raw JSON is the first argument; the text renderer is a no-arg lambda.

**Fix:** Change to `OutputFormatter.WriteRaw(json, () => { OutputWriter.WriteLine(PrettyPrint(json)); });` — capture `json` from the enclosing scope.

---

### B2. `SolutionDetail` record definition inconsistency between Phase 0 and Phase 1

**Phase 0 (00-infrastructure.md, line ~377–388):**
```csharp
public sealed record SolutionDetail(
    Guid Id, string UniqueName, string? FriendlyName, string? Description,
    string? Version, bool Managed, string? PublisherId, string? PublisherName,
    DateTime? InstalledOn, DateTime? ModifiedOn);
```

**Phase 1 (01-inspection.md, line ~120–129):**
```csharp
public sealed record SolutionDetail(
    Guid Id, string UniqueName, string? FriendlyName, string? Version,
    bool Managed, DateTime? InstalledOn, string? Description,
    string? PublisherName, string? PublisherPrefix);
```

These are **different types**: different parameter order, Phase 0 has `PublisherId` + `ModifiedOn`, Phase 1 has `PublisherPrefix` instead. Since the Phase 0 stubs are created first and Phase 1 replaces them, implementers following Phase 0 literally will create a different record than Phase 1 expects. The service implementation in Phase 1 constructs the Phase 1 version, which won't compile against the Phase 0 interface return type.

**Fix:** Canonicalize to one definition. Phase 1 is more complete (has `PublisherPrefix` which is actually fetched). Use Phase 1's version everywhere.

---

### B3. `ISolutionManagementService` interface definition conflict between phases

**Phase 0 (00-infrastructure.md, line ~410–423):**
```csharp
public interface ISolutionManagementService
{
    Task<SolutionGetResult?> GetAsync(...);
    Task<Guid> CreateAsync(..., SolutionCreateOptions options, ...);
    Task DeleteAsync(...);
    Task PublishAsync(string? profileName, CancellationToken ct);
}
```

**Phase 1 (01-inspection.md, line ~141–151):**
```csharp
public interface ISolutionManagementService
{
    Task<(SolutionDetail Solution, IReadOnlyList<ComponentCountRow> Counts)> GetAsync(
        string? profileName, string solutionUniqueName, CancellationToken ct);
}
```

Phase 0's `GetAsync` returns `SolutionGetResult?`. Phase 1's returns a **value tuple**. These are incompatible return types. The Phase 0 stub service implements `SolutionGetResult?` but the Phase 1 service implementation returns a tuple.

**Phase 2 (02-crud.md)** also has its own `SolutionCreateOptions` with `DisplayName` vs Phase 0's `FriendlyName`, and Phase 2's `PublishAsync` takes `IReadOnlyList<string>? entityLogicalNames` while Phase 0's takes no such parameter.

**Fix:** Pick ONE canonical interface. The Phase 1 tuple approach is simpler for the `GetAsync` caller. Reconcile `CreateAsync` and `PublishAsync` signatures across Phase 0 and Phase 2.

---

### B4. `ISolutionComponentService` interface definition conflict between phases

**Phase 0 (00-infrastructure.md, line ~447–468):**
- `CountAsync` takes `Guid solutionId`
- `ListAsync` takes `Guid solutionId, int? typeCodeFilter`
- `AddAsync` takes flat parameters
- `RemoveAsync` takes flat parameters

**Phase 1 (01-inspection.md, line ~348–369):**
- `CountAsync` takes `Guid solutionId` ✅ (matches)
- `ListAsync` takes `Guid solutionId, int? componentTypeFilter, int? top` — Phase 0 has no `top` param

**Phase 3 (03-mutations.md, line ~43–46):**
- `AddAsync` returns `Task<ComponentAddOutcome>` taking `ComponentAddOptions options` (a DTO)
- `RemoveAsync` returns `Task<ComponentRemoveOutcome>` taking `ComponentRemoveOptions options`

These conflict with Phase 0's flat-parameter signatures that return `Task` (void).

**Fix:** Decide on one approach — DTOs (Phase 3) or flat params (Phase 0). Phase 3's DTO approach is more extensible.

---

### B5. `ISolutionDependencyService` interface definition conflict

**Phase 0 (00-infrastructure.md, line ~498–515):**
- Returns `DependencyRecord` (with fields `DependentObjectId`, `RequiredObjectId`)
- `CheckDeleteAsync` / `CheckUninstallAsync` return `DependencyCheckResult`

**Phase 1 (01-inspection.md, line ~646–691):**
- Returns `DependencyRow` (with fields `DependentComponentId`, `RequiredComponentId`)
- `CheckDeleteAsync` / `CheckUninstallAsync` return `IReadOnlyList<DependencyRow>`

Different DTO names, different field names, different return types for the check methods. Will not compile if Phase 0 creates one definition and Phase 1 expects another.

**Fix:** Use Phase 1's definitions as canonical — they're simpler and match the actual implementation code.

---

### B6. `ISolutionLayerService` interface definition conflict

**Phase 0 (00-infrastructure.md, line ~538–551):**
- `ListLayersAsync` takes `Guid componentId, int componentType`
- `GetActiveLayerAsync` takes `Guid componentId, int componentType`
- `RemoveCustomizationAsync` takes `Guid componentId, int componentType`
- Returns `ComponentLayerRecord`

**Phase 1 (01-inspection.md, line ~933–952):**
- `ListLayersAsync` takes `string componentId, string componentTypeName`
- `GetActiveLayerJsonAsync` takes `string componentId, string componentTypeName`
- Returns `ComponentLayerRow`

Phase 0 uses `Guid`+`int` params; Phase 1 uses `string`+`string`. Phase 0 returns `ComponentLayerRecord`; Phase 1 returns `ComponentLayerRow`. The service implementation in Phase 1 passes strings directly to Web API URLs, which is correct for the OData filter. If Phase 0 defines the interface with `Guid`/`int`, Phase 1's implementation won't compile.

**Fix:** Use Phase 1's string-based signatures as canonical — they match the Web API usage pattern.

---

### B7. Phase 2 `SolutionCreateOptions` has `DisplayName` but Phase 0 has `FriendlyName`

**Phase 0 (00-infrastructure.md, line ~398–403):**
```csharp
public sealed record SolutionCreateOptions(
    string UniqueName, string FriendlyName, string? Description,
    string? Version, Guid PublisherId);
```

**Phase 2 (02-crud.md, line ~53–57):**
```csharp
public sealed record SolutionCreateOptions(
    string UniqueName, string DisplayName, string PublisherUniqueName,
    string Version, string? Description);
```

Different field names (`FriendlyName` vs `DisplayName`), different publisher resolution (`Guid PublisherId` vs `string PublisherUniqueName`), different nullability, different parameter order.

**Fix:** Use Phase 2's version — it has `PublisherUniqueName` (resolved server-side in the SDK class), which is more user-friendly.

---

### B8. `SolutionUninstallOutcome` modification will break existing code

**Plan (02-crud.md, line ~314–319):**
Proposes changing `SolutionUninstallOutcome` to add `IReadOnlyList<SolutionDependencyRecord>? BlockingDependencies = null` and a `HasDependencies` enum value.

**Actual (`ISolutionUninstallService.cs`):**
```csharp
public sealed record SolutionUninstallOutcome(
    string SolutionName, Guid? SolutionId,
    SolutionUninstallStatus Status, string Message);
```

Adding a new required-ish constructor parameter (even with default) to a positional record changes how all existing construction sites work. The existing `SolutionUninstaller.cs` creates these outcomes at lines 45, 50, 62 — all would need updating.

**Severity:** Manageable but must be done atomically. Not truly "breaking" if implemented carefully, but the plan doesn't mention updating the 4+ existing construction sites in `SolutionUninstaller.cs`.

---

### B9. `comp export` marked `[CliReadOnly]` but creates and deletes a temp solution

**Plan (01-inspection.md, line ~1439):**
```csharp
[CliReadOnly] // Temp solution is created and deleted — no persistent side effects.
```

**Actual analyzer enforcement:** The codebase has a `MustDeclareAccessLevelAnalyzer` that enforces safety attributes. While the _intent_ is read-only (no persistent changes), this command performs `CreateRequest` + `DeleteRequest` — it IS mutative during execution. If the command crashes between create and delete, the temp solution persists. This should be `[CliIdempotent]` at minimum.

**Fix:** Use `[CliIdempotent]` for `comp export` — it has side effects but re-running is safe.

---

## 🟡 Inconsistency — Doesn't match patterns, will create tech debt

### I1. Service implementation architecture: plan mixes two patterns

**Existing pattern (e.g., `DataverseSolutionUninstallService`):**
- Service implementation in `Services/` is a thin wrapper
- Calls `DataverseCommandBridge.ConnectAsync()` to get a connection
- Delegates to an SDK class (e.g., `SolutionUninstaller`) that takes `IOrganizationServiceAsync2`
- SDK class has no DI, no `profileName` — pure SDK logic

**Plan Phase 1 (01-inspection.md):**
- Service implementation in `Services/` does EVERYTHING inline — connection, SDK calls, JSON parsing
- No separate SDK class for most operations
- Only `comp export` (PR 4) and Phase 3 mutations mention SDK classes (`SolutionComponentManager`, `ComponentLayerManager`)

**Impact:** Phase 1 services like `DataverseSolutionManagementService` and `DataverseSolutionComponentService` put SDK logic directly in the service class. This is inconsistent with the uninstall/import pattern where SDK logic lives in `Sdk/` classes. It means these services can't be unit-tested with a mocked `IOrganizationServiceAsync2` without also mocking `DataverseCommandBridge`.

**Recommendation:** Follow the existing two-layer pattern consistently. Create SDK helper classes for the core logic.

---

### I2. New services in Phase 1 don't use logger field pattern from existing stubs

**Phase 0 stub (00-infrastructure.md, line ~722–723):**
```csharp
internal sealed class DataverseSolutionManagementService : ISolutionManagementService
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(...));
```

**Phase 1 implementation (01-inspection.md, line ~168):**
```csharp
internal sealed class DataverseSolutionManagementService : ISolutionManagementService
{
    // No logger field — just raw implementation
```

Existing service implementations (e.g., `DataverseSolutionUninstallService`) have `_logger`. The Phase 1 implementations omit it.

---

### I3. `ComponentCountRow` vs `ComponentTypeCount` — same concept, two names

**Phase 0 (00-infrastructure.md):** Defines `ComponentTypeCount(int TypeCode, string TypeName, int Count)`
**Phase 1 (01-inspection.md):** Defines `ComponentCountRow(string TypeName, int TypeCode, string? LogicalName, int Count)`

Different name, different field order, Phase 1 adds `LogicalName`. The plan says "reuse it" but the definitions are incompatible.

**Fix:** Define once, in one file. Use Phase 1's version (it has the extra field needed by the service implementation).

---

### I4. `msdyn_total` vs `msdyn_componentcount` for count summary

**Phase 0 schema constants (00-infrastructure.md):**
```csharp
public const string ComponentCount = "msdyn_componentcount";
```

**Phase 1 implementation (01-inspection.md, line ~223, ~409):**
```csharp
Count: item.GetProperty("msdyn_total").GetInt32()
```

The plan defines a constant `msdyn_componentcount` but the actual implementation uses `msdyn_total`. One of these is wrong — the field name in the virtual entity response needs verification. The constant is never used.

**Fix:** Verify the correct field name against the actual Dataverse API response and align.

---

### I5. Dependency commands use `ExecuteWebRequest` but plan says "SDK-first"

**README (line 11):**
> **SDK-first** — all operations through `IOrganizationServiceAsync2`; `ExecuteWebRequest` for Web API-only virtual entities

**Phase 0 Key Decision table (line ~757–761):**
> Dependency queries → SDK (`RetrieveDependentComponentsRequest`) — Typed messages available

**Phase 1 implementation (01-inspection.md, line ~716–717):**
```csharp
var path = $"RetrieveDependenciesForUninstall(SolutionUniqueName='{solutionUniqueName}')";
var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
```

The plan says dependency queries should use SDK typed messages, but the actual implementation code in Phase 1 uses Web API function calls. Both work, but this contradicts the stated principle and the Phase 0 decision table. The Phase 2 uninstall enhancement (02-crud.md, line ~269) correctly uses `RetrieveDependenciesForUninstallRequest` (SDK).

**Fix:** Pick one approach. The Web API approach in Phase 1 is simpler (no need for `Microsoft.CrmSdk.Messages` dependency). Update the Phase 0 decision table or change the Phase 1 implementation.

---

### I6. `SolutionComponentRemoveCliCommand` file path inconsistency

**Phase 3 (03-mutations.md, line ~130):** Places the file at:
`src/TALXIS.CLI.Features.Environment/Solution/SolutionComponentRemoveCliCommand.cs`

But the command is `txc env sln component remove` — it's a child of the `component` subcommand. Following the established pattern from Phase 1 (where `SolutionComponentListCliCommand` is in `Solution/Component/`), this should be:
`src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentRemoveCliCommand.cs`

Same issue for `SolutionComponentAddCliCommand`.

**Fix:** Move to `Solution/Component/` subfolder to match Phase 1's structure.

---

### I7. `ComponentLayerRemoveCustomizationCliCommand` goes under `Component/` but Phase 3 says `Component/Layer/`

**Phase 3 (03-mutations.md):** Correctly identifies the path as `Component/ComponentLayerRemoveCustomizationCliCommand.cs` but this should be `Component/Layer/ComponentLayerRemoveCustomizationCliCommand.cs` to match the Phase 1 layer command structure where `ComponentLayerListCliCommand` and `ComponentLayerGetCliCommand` live in `Component/Layer/`.

---

### I8. `--type` parameter inconsistency: int vs string across commands

**Phase 1 layer commands:** `--type` is `string` (e.g., "Entity", "Workflow")
**Phase 1 dependency commands:** `--type` is `int`
**Phase 3 mutation commands:** `--type` is `string` (resolved via `ComponentTypeResolver`)

Some commands accept friendly names, others require raw int codes. Users will find this confusing.

**Fix:** Always accept both (string OR int) via `ComponentTypeResolver`. Parse as int first; if that fails, resolve by name.

---

### I9. `ExecuteWebRequest` usage doesn't check HTTP status codes

**Actual codebase pattern (`DataverseQueryService.cs`):**
```csharp
var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, queryPath, string.Empty, headers);
```
No `response.EnsureSuccessStatusCode()` or status check.

**Plan's service implementations:** Same pattern — no error checking on the HTTP response. If the Web API returns 404 or 400, the code will try to parse the error body as valid JSON and throw a confusing `JsonException`.

**Fix:** Add `response.EnsureSuccessStatusCode()` or at least check `response.IsSuccessStatusCode` before parsing in the new service implementations. (Note: this is a pre-existing pattern issue — the plan faithfully copies it — but new code should be better.)

---

### I10. `SolutionCreateOptions.PublisherId` is `Guid` in Phase 0 but Phase 2's SDK code resolves name→GUID

**Phase 0 (00-infrastructure.md):** `SolutionCreateOptions` has `Guid PublisherId`
**Phase 2 (02-crud.md):** The CLI command takes `--publisher` as a unique name string, and the SDK class resolves it to a GUID.

If the DTO carries a `Guid`, the CLI command must resolve the name→GUID before constructing the DTO. But Phase 2's SDK logic does the resolution itself. This means either:
- The CLI command resolves (extra connection just for resolution), or
- The DTO should carry `string PublisherUniqueName` (Phase 2's version)

**Fix:** Use `string PublisherUniqueName` in the DTO. Let the SDK class handle resolution.

---

### I11. Missing `Microsoft.CrmSdk.Messages` package reference

**Phase 2 (02-crud.md)** and **Phase 3 (03-mutations.md)** reference SDK messages like `AddSolutionComponentRequest`, `RemoveSolutionComponentRequest`, `RemoveActiveCustomizationsRequest`, `PublishAllXmlRequest`, `PublishXmlRequest`, `ExportSolutionRequest`, `RetrieveDependenciesForUninstallRequest`.

**Actual csproj:** Only references `Microsoft.PowerPlatform.Dataverse.Client` version `1.2.10`. The `Microsoft.Crm.Sdk.Messages` types are typically in the `Microsoft.CrmSdk.CoreAssemblies` NuGet package.

**However:** `Microsoft.PowerPlatform.Dataverse.Client` transitively depends on `Microsoft.CrmSdk.CoreAssemblies` which includes `Microsoft.Crm.Sdk.Messages`. The existing code (`SolutionImporter.cs`) uses types from that assembly. So this should work — but the plan should verify these specific message types are available in the transitively-referenced version.

**Risk:** Medium. The messages like `RemoveActiveCustomizationsRequest` were added in later SDK versions. Verify against the specific version pulled by `Dataverse.Client 1.2.10`.

---

## 🟢 Suggestion — Plan is fine but could be improved

### S1. Consider `WriteResult` for mutative command outcomes

**Plan (Phase 2, Phase 3):** Uses `OutputFormatter.WriteData(outcome)` or custom `RenderSingle` methods for create/publish/add/remove outcomes.

**Actual (`OutputFormatter.cs`):** Has a purpose-built `WriteResult(status, message?, id?)` method for mutative commands. This provides a consistent JSON envelope `{ status, message, id }`.

**Suggestion:** Use `OutputFormatter.WriteResult()` for mutative commands (create, publish, add, remove) to get consistent output structure.

---

### S2. `EnvironmentCliCommand.Children` already has 6 children — consider grouping

**Actual `EnvironmentCliCommand`:**
```csharp
Children = new[] { typeof(Package...), typeof(Solution...), typeof(Deployment...),
                   typeof(Data...), typeof(Entity...), typeof(Setting...) }
```

Adding `typeof(Component.ComponentCliCommand)` makes it 7 top-level subcommands. This is fine — the plan correctly identifies `component` as a peer of `solution`. No issue, just noting that the help output will be getting long.

---

### S3. Phase 0 creates SolutionPackager NuGet reference that Phase 1 doesn't need

The SolutionPackager reference (`Microsoft.PowerApps.CLI.Core.linux-x64`) and `SolutionPackagerService.cs` are Phase 0 infrastructure but are only used by `comp export` in Phase 1 PR 4. Consider deferring the NuGet reference to PR 4 to keep Phase 0 lighter. The `<Reference Include="SolutionPackagerLib">` approach with `HintPath` is unusual for this project (no other `<Reference>` elements exist in the csproj) and may cause build issues in CI if the NuGet package layout changes.

---

### S4. `ComponentTypeResolver` is not DI-registered but needs `ServiceClient` for SCF types

**Plan (00-infrastructure.md, line ~234):**
> This class is **not** a DI service — it's instantiated per-command in SDK classes.

But `LoadScfTypesAsync` requires a `ServiceClient`. SDK classes receive `IOrganizationServiceAsync2`, not `ServiceClient`. The plan acknowledges casting `IOrganizationServiceAsync2 → ServiceClient` but this is fragile — tests using mocked services will break.

**Suggestion:** Consider making `LoadScfTypesAsync` accept `IOrganizationServiceAsync2` and handle the cast internally with a fallback (skip SCF loading if not a `ServiceClient`).

---

### S5. `CancellationToken.None` used everywhere in CLI commands

All plan CLI commands pass `CancellationToken.None`:
```csharp
await service.GetAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);
```

The `DotMake` framework may provide a cancellation token via the `CliContext`. Using `CancellationToken.None` means Ctrl+C won't cancel ongoing operations gracefully. This is consistent with existing commands (e.g., `SolutionListCliCommand` does the same), so it's not a plan-specific issue — but new commands could improve on this.

---

### S6. Plan references `conn.Client` on `DataverseCommandBridge` return value

The plan and existing code both use:
```csharp
using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
// then: conn.Client.ExecuteWebRequest(...) or conn.Client.RetrieveMultipleAsync(...)
```

This pattern is correct and consistent. ✅ No issue here.

---

### S7. `ComponentSummaryRow.Managed` parsed as `bool` from Web API `msdyn_ismanaged`

**Plan (01-inspection.md, line ~449):**
```csharp
Managed: item.TryGetProperty("msdyn_ismanaged", out var m) && m.GetBoolean(),
```

Virtual entity fields often return string representations (`"true"/"false"`) rather than JSON booleans. If `msdyn_ismanaged` comes back as a string, `m.GetBoolean()` will throw.

**Suggestion:** Add defensive parsing: `bool.TryParse(m.GetString(), out var managed) ? managed : false`.

---

### S8. DI registers all 4 new services as `Singleton` — connection is per-call anyway

**Plan (00-infrastructure.md, line ~693–696):**
```csharp
services.AddSingleton<ISolutionManagementService, DataverseSolutionManagementService>();
```

This matches the existing pattern (`DataverseSolutionInventoryService` is also singleton). Since each method call creates its own `DataverseCommandBridge` connection, singleton is fine. ✅ No issue — just confirming the pattern is correct.

---

## Summary

| Severity | Count | Key Themes |
|----------|-------|------------|
| 🔴 Breaking | 9 | DTO/interface definition conflicts between Phase 0 and Phase 1; `WriteRaw` signature; safety attribute misuse |
| 🟡 Inconsistency | 11 | Mixed architecture patterns; parameter type inconsistencies; file path mismatches |
| 🟢 Suggestion | 8 | Output helpers; defensive parsing; NuGet reference timing |

### Top 3 Actions Before Coding

1. **Canonicalize all interface + DTO definitions.** Phase 0 and Phase 1 define the same types differently. Pick Phase 1's versions as canonical (they match the actual implementation code) and update Phase 0 accordingly.

2. **Decide SDK vs Web API for dependency queries.** The plan contradicts itself — Phase 0 says SDK, Phase 1 implements Web API. Pick one.

3. **Fix the `OutputFormatter.WriteRaw` call.** The `Action<string>` signature doesn't exist — use `Action` and capture from scope.
