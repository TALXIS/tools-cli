# Phase 0 — Shared Infrastructure

Prerequisites that all subsequent phases depend on. This milestone creates the foundational classes, service contracts, schema constants, and DI wiring that **every** solution-management command will use. Nothing in Phases 1–4 should start until this PR is merged.

---

## Table of Contents

1. [Dependency Order](#dependency-order)
2. [Schema Constants](#1-schema-constants)
3. [ComponentTypeResolver](#2-componenttyperesolver)
4. [ComponentNameResolver](#3-componentnameresolver)
5. [Service Contracts & DTOs](#4-service-contracts--dtos)
6. [SolutionPackager Integration](#5-solutionpackager-integration)
7. [DI Registration](#6-di-registration)
8. [Key Decision: SDK vs Web API](#key-decision-sdk-vs-web-api)
9. [PR Scope](#pr-scope)
10. [Testing Approach](#testing-approach)

---

## Dependency Order

Complete these tasks in sequence — each step depends on the one above it:

```
1. Schema Constants          (no dependencies)
2. ComponentTypeResolver     (depends on schema constants)
3. ComponentNameResolver     (depends on ComponentTypeResolver + schema constants)
4. Service Contracts & DTOs  (no code dependencies, but design informed by resolvers)
5. SolutionPackager           (independent — can be done in parallel with 2–4)
6. DI Registration            (depends on all of the above)
```

---

## 1. Schema Constants

### What to do

Extend the existing `DataverseSchema.cs` with nested classes for all solution-management entities. Follow the exact pattern already used for `Solution`, `AsyncOperation`, and `ImportJob`.

### File to modify

`src/TALXIS.CLI.Platform.Dataverse.Application/Domain/DataverseSchema.cs`

### Changes

Add these nested classes **inside** the existing `DataverseSchema` static class, below the existing `ImportJob` class:

```csharp
// ── Solution Management ────────────────────────────────────────────

/// <summary>Row in the solutioncomponent table (links a component to a solution).</summary>
public static class SolutionComponent
{
    public const string EntityName = "solutioncomponent";
    public const string ComponentType = "componenttype";
    public const string ObjectId = "objectid";
    public const string SolutionId = "solutionid";
    public const string RootComponentBehavior = "rootcomponentbehavior";
    public const string IsMetadata = "ismetadata";
}

/// <summary>Virtual entity — active/managed solution layers on a component.</summary>
public static class MsdynComponentLayer
{
    public const string EntityName = "msdyn_componentlayer";
    public const string ComponentId = "msdyn_componentid";
    public const string Name = "msdyn_name";
    public const string SolutionComponentName = "msdyn_solutioncomponentname";
    public const string SolutionName = "msdyn_solutionname";
    public const string Order = "msdyn_order";
    public const string EndTime = "msdyn_endtime";
    public const string Children = "msdyn_children";
}

/// <summary>Virtual entity — rich component metadata for display purposes.</summary>
public static class MsdynSolutionComponentSummary
{
    public const string EntityName = "msdyn_solutioncomponentsummary";
    public const string ObjectId = "msdyn_objectid";
    public const string ComponentType = "msdyn_componenttype";
    public const string DisplayName = "msdyn_displayname";
    public const string Name = "msdyn_name";
    public const string SolutionId = "msdyn_solutionid";
    public const string IsManaged = "msdyn_ismanaged";
    public const string SchemaName = "msdyn_schemaname";
    public const string PrimaryEntityName = "msdyn_primaryentityname";
}

/// <summary>Virtual entity — component type counts per solution.</summary>
public static class MsdynSolutionComponentCountSummary
{
    public const string EntityName = "msdyn_solutioncomponentcountsummary";
    public const string ComponentType = "msdyn_componenttype";
    public const string ComponentTypeName = "msdyn_componenttypename";
    public const string ComponentCount = "msdyn_total";
    public const string SolutionId = "msdyn_solutionid";
}

/// <summary>Row returned by dependency SDK messages (RetrieveDependentComponents etc.).</summary>
public static class Dependency
{
    public const string EntityName = "dependency";
    public const string DependentComponentObjectId = "dependentcomponentobjectid";
    public const string DependentComponentType = "dependentcomponenttype";
    public const string RequiredComponentObjectId = "requiredcomponentobjectid";
    public const string RequiredComponentType = "requiredcomponenttype";
    public const string DependencyType = "dependencytype";
}
```

### How to verify

- Build the project: `dotnet build src/TALXIS.CLI.Platform.Dataverse.Application/`.
- Confirm no compile errors — the constants are just `const string` fields so no runtime test is needed.

---

## 2. ComponentTypeResolver

Maps between integer type codes and friendly names. Supports both platform components (static codes) and SCF components (runtime codes queried from `solutioncomponentdefinitions`).

> **Note:** All CLI commands that accept a `--type` parameter should route through `ComponentTypeResolver`, which accepts both integer codes (e.g., `1`) and friendly string names (e.g., `Entity`, `Table`). Parse as int first; if that fails, resolve by name. This ensures a consistent UX across all commands.

### File to create

`src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/ComponentTypeResolver.cs`

### Platform types (hardcoded)

| Code | Platform Name | Friendly Names |
|------|--------------|----------------|
| 1 | Entity | `Entity`, `Table` |
| 2 | Attribute | `Attribute`, `Column` |
| 9 | OptionSet | `OptionSet`, `Choice` |
| 10 | EntityRelationship | `EntityRelationship` |
| 14 | EntityKey | `EntityKey` |
| 20 | Role | `Role`, `SecurityRole` |
| 26 | SavedQuery | `View` |
| 29 | Workflow | `Workflow`, `Process` |
| 59 | SavedQueryVisualization | `Chart` |
| 60 | SystemForm | `Form`, `Dashboard` |
| 61 | WebResource | `WebResource` |
| 62 | SiteMap | `SiteMap` |
| 91 | PluginAssembly | `PluginAssembly` |
| 92 | SDKMessageProcessingStep | `PluginStep` |
| 300 | CanvasApp | `CanvasApp` |
| 380 | EnvironmentVariableDefinition | `EnvironmentVariable` |

**SCF types (dynamic):** Query `solutioncomponentdefinitions?$select=name,objecttypecode` at runtime. SCF type codes are runtime-assigned and can differ between environments — always resolve by name.

### Code sketch

```csharp
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Bidirectional mapping between solution-component integer type codes and
/// human-readable names.  Platform component codes are hardcoded; SCF
/// (Solution Component Framework) codes are resolved at runtime via the
/// <c>solutioncomponentdefinition</c> virtual entity.
/// </summary>
public sealed class ComponentTypeResolver
{
    /// <summary>Immutable entry describing one component type.</summary>
    public sealed record ComponentTypeInfo(int Code, string PlatformName, IReadOnlyList<string> FriendlyNames);

    // Hardcoded platform types — populate in a static readonly dictionary.
    // Key = type code (int).
    private static readonly Dictionary<int, ComponentTypeInfo> PlatformTypes = BuildPlatformTypes();

    // Runtime SCF types fetched once per resolver instance.
    // Key = type code (int). Populated by LoadScfTypesAsync().
    private Dictionary<int, ComponentTypeInfo>? _scfTypes;

    // Also maintain a reverse lookup: friendly-name (case-insensitive) → code.
    // Built after SCF types are loaded.
    private Dictionary<string, int>? _nameToCode;

    /// <summary>
    /// Queries <c>solutioncomponentdefinitions</c> via Web API and caches the results.
    /// Call once per command execution before resolving any types.
    /// Accepts <see cref="IOrganizationServiceAsync2"/> for a consistent public API;
    /// internally casts to <see cref="ServiceClient"/> for Web API access.
    /// </summary>
    public async Task LoadScfTypesAsync(IOrganizationServiceAsync2 service, CancellationToken ct = default)
    {
        // SOLID fix (D1): Accept abstraction, cast internally with a clear guard.
        if (service is not ServiceClient client)
            throw new InvalidOperationException("SCF type resolution requires a ServiceClient instance.");

        // GET solutioncomponentdefinitions?$select=name,objecttypecode
        // Parse the JSON response into _scfTypes dictionary.
        // Build _nameToCode from both PlatformTypes and _scfTypes.
        throw new NotImplementedException();
    }

    /// <summary>Resolves a type code to its info. Returns null if unknown.</summary>
    public ComponentTypeInfo? Resolve(int typeCode)
    {
        if (PlatformTypes.TryGetValue(typeCode, out var info)) return info;
        return _scfTypes?.GetValueOrDefault(typeCode);
    }

    /// <summary>
    /// Resolves a friendly name (case-insensitive) to a type code.
    /// Useful for CLI --type options where users type "Entity" or "Table".
    /// </summary>
    public int? ResolveByName(string friendlyName)
    {
        // Lookup in _nameToCode (built from both platform + SCF types).
        throw new NotImplementedException();
    }

    /// <summary>Friendly display name for a type code (first friendly name, or "Unknown (code)").</summary>
    public string GetDisplayName(int typeCode)
    {
        var info = Resolve(typeCode);
        return info?.FriendlyNames.FirstOrDefault() ?? info?.PlatformName ?? $"Unknown ({typeCode})";
    }

    private static Dictionary<int, ComponentTypeInfo> BuildPlatformTypes()
    {
        // Build dictionary from the hardcoded table above.
        // Each entry: new ComponentTypeInfo(code, platformName, new[] { friendlyName1, friendlyName2 })
        // Example: { 1, new ComponentTypeInfo(1, "Entity", new[] { "Entity", "Table" }) }
        throw new NotImplementedException();
    }
}
```

### Implementation notes

- `LoadScfTypesAsync` accepts `IOrganizationServiceAsync2` and casts to `ServiceClient` internally with a guarded check (SOLID fix D1). Uses `ServiceClient.ExecuteWebRequest(HttpMethod.Get, "solutioncomponentdefinitions?$select=name,objecttypecode", ...)`. Parse with `System.Text.Json`. This keeps the public API abstract while acknowledging the Web API limitation — same pattern as `DataverseQueryService`.
- The reverse name lookup dictionary should be populated from **both** platform types and SCF types (SCF overwrites on collision).
- This class is **not** a DI service — it's instantiated per-command in SDK classes that need it (like `ComponentNameResolver`). The constructor takes no parameters; `LoadScfTypesAsync` is called explicitly.

### How to verify

- Unit test `BuildPlatformTypes()` — check all 16 entries are present.
- Unit test `Resolve(1)` returns `Entity` / `Table`.
- Unit test `ResolveByName("Table")` returns `1`.
- Integration test: `LoadScfTypesAsync` against a real environment (optional, manual).

---

## 3. ComponentNameResolver

Enriches raw dependency output (GUIDs + type codes) with display names. The `WithMetadata` Web API variants that the portal uses returned empty arrays in testing, so we need our own resolver.

### File to create

`src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/ComponentNameResolver.cs`

### Resolution strategy by type

| Component Type | Lookup Method |
|---|---|
| Entities (code 1) | `EntityDefinitions` metadata query via SDK |
| Forms (60), Views (26), Charts (59) | Query their respective entity tables by GUID |
| Solutions (referenced by GUID) | Batch query `solution` table |
| General fallback | `msdyn_solutioncomponentsummaries?$filter=msdyn_objectid eq '{id}'` (Web API) |

### Code sketch

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Resolves solution-component GUIDs + type codes into human-readable display
/// names.  Caches results so repeated lookups within a single command
/// execution don't generate extra requests.
/// </summary>
public sealed class ComponentNameResolver
{
    private readonly IOrganizationServiceAsync2 _service;
    private readonly ComponentTypeResolver _typeResolver;
    private readonly ILogger? _logger;

    // Cache: (objectId, typeCode) → display name.
    // Thread-safety not required — used within a single command execution.
    private readonly Dictionary<(Guid ObjectId, int TypeCode), string> _cache = new();

    public ComponentNameResolver(
        IOrganizationServiceAsync2 service,
        ComponentTypeResolver typeResolver,
        ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _logger = logger;
    }

    /// <summary>
    /// Resolves a single component to its display name. Checks cache first.
    /// </summary>
    public async Task<string> ResolveAsync(Guid objectId, int typeCode, CancellationToken ct = default)
    {
        if (_cache.TryGetValue((objectId, typeCode), out var cached))
            return cached;

        var name = await ResolveUncachedAsync(objectId, typeCode, ct).ConfigureAwait(false);
        _cache[(objectId, typeCode)] = name;
        return name;
    }

    /// <summary>
    /// Batch-resolves multiple components. Preferred over calling ResolveAsync
    /// in a loop — groups lookups by type for efficient batching.
    /// </summary>
    public async Task<IReadOnlyDictionary<(Guid ObjectId, int TypeCode), string>> ResolveBatchAsync(
        IEnumerable<(Guid ObjectId, int TypeCode)> components,
        CancellationToken ct = default)
    {
        // 1. Split into cached vs. uncached.
        // 2. Group uncached by typeCode.
        // 3. For each group, call the appropriate bulk lookup (e.g., batch
        //    RetrieveMultiple for entities, single Web API call for summaries).
        // 4. Merge results into _cache and return.
        throw new NotImplementedException();
    }

    private async Task<string> ResolveUncachedAsync(Guid objectId, int typeCode, CancellationToken ct)
    {
        // Switch on typeCode:
        //   case 1 (Entity): RetrieveEntityRequest with MetadataId = objectId
        //                     → return entity.DisplayName or entity.LogicalName
        //   case 26 (View):  RetrieveMultiple on savedquery by savedqueryid
        //   case 59 (Chart): RetrieveMultiple on savedqueryvisualization
        //   case 60 (Form):  RetrieveMultiple on systemform by formid
        //   default:         FallbackResolveBySummaryAsync()
        throw new NotImplementedException();
    }

    /// <summary>
    /// Fallback: query msdyn_solutioncomponentsummary virtual entity via Web API.
    /// </summary>
    private async Task<string> FallbackResolveBySummaryAsync(Guid objectId, CancellationToken ct)
    {
        // Use ServiceClient.ExecuteWebRequest:
        //   GET msdyn_solutioncomponentsummaries?$filter=msdyn_objectid eq '{objectId}'
        //       &$select=msdyn_displayname,msdyn_name
        // Return msdyn_displayname ?? msdyn_name ?? objectId.ToString()
        throw new NotImplementedException();
    }
}
```

### Implementation notes

- Constructor takes `IOrganizationServiceAsync2` (not `ServiceClient`) for consistency with `SolutionImporter` / `SolutionUninstaller`. For Web API calls (e.g., `FallbackResolveBySummaryAsync`), cast to `ServiceClient` internally with a guarded check (SOLID fix D2): `if (service is not ServiceClient client) throw new InvalidOperationException(...)`. This keeps the public contract abstract — same pattern as `ComponentTypeResolver.LoadScfTypesAsync`.
- The cache uses a `Dictionary<(Guid, int), string>` — **not** `ConcurrentDictionary`. The resolver lives within a single command execution and is not shared across threads.
- `ResolveBatchAsync` is the preferred entry point. Phase 1 dependency commands return collections of `(objectId, typeCode)` tuples — batch them all in one call.

### How to verify

- Unit test: verify cache hit returns immediately without calling the service.
- Unit test: verify `ResolveBatchAsync` groups by type correctly (mock `IOrganizationServiceAsync2`).
- Integration test: resolve a known Entity GUID, a known Form GUID (optional, manual).

---

## 4. Service Contracts & DTOs

New interfaces in `src/TALXIS.CLI.Core/Contracts/Dataverse/`. Follow the exact pattern of `ISolutionImportService.cs` — DTOs as records at the top, interface at the bottom, all in one file per service.

### 4.1 `ISolutionDetailService.cs`

> **SOLID fix (S1/I1):** The original `ISolutionManagementService` bundled 4 unrelated operations (show, create, delete, publish) behind one interface. This violated SRP (4 reasons to change) and ISP (read-only commands forced to depend on mutative methods). Split into focused single-method interfaces matching the established `ISolutionImportService` pattern. Delete is handled by the existing `ISolutionUninstallService`. See [SOLID audit report](./research/solid-audit-report.md) findings S1, I1.

**File to create:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionDetailService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Detailed solution info returned by <see cref="ISolutionDetailService.GetAsync"/>.
/// Canonical definition — matches Phase 1 implementation.
/// </summary>
public sealed record SolutionDetail(
    Guid Id,
    string UniqueName,
    string? FriendlyName,
    string? Version,
    bool Managed,
    DateTime? InstalledOn,
    string? Description,
    string? PublisherName,
    string? PublisherPrefix);

/// <summary>
/// Per-component-type count row returned alongside <see cref="SolutionDetail"/>.
/// Reused by <see cref="ISolutionComponentQueryService.CountAsync"/>.
/// Canonical definition — matches Phase 1 implementation.
/// </summary>
public sealed record ComponentCountRow(
    string TypeName,
    int TypeCode,
    string? LogicalName,
    int Count);

/// <summary>
/// Read-only solution detail retrieval.
/// Phase 0 defines the contract; Phase 1 implements GetAsync.
/// </summary>
public interface ISolutionDetailService
{
    /// <summary>Gets solution details + component type counts.</summary>
    Task<(SolutionDetail Solution, IReadOnlyList<ComponentCountRow> Counts)> GetAsync(
        string? profileName, string solutionUniqueName, CancellationToken ct);
}
```

### 4.1b `ISolutionCreateService.cs`

**File to create:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionCreateService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>Options for creating a new unmanaged solution.</summary>
public sealed record SolutionCreateOptions(
    string UniqueName,
    string DisplayName,
    string PublisherUniqueName,
    string Version,
    string? Description);

/// <summary>Result of a solution create operation.</summary>
public sealed record SolutionCreateOutcome(
    string UniqueName,
    Guid SolutionId,
    string Status,   // "Created" or "AlreadyExists"
    string Message);

/// <summary>
/// Creates unmanaged solutions. Phase 0 defines the contract; Phase 2 implements CreateAsync.
/// </summary>
public interface ISolutionCreateService
{
    /// <summary>Creates an unmanaged solution.</summary>
    Task<SolutionCreateOutcome> CreateAsync(string? profileName, SolutionCreateOptions options, CancellationToken ct);
}
```

### 4.1c `ISolutionPublishService.cs`

**File to create:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionPublishService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Publishes customizations. Phase 0 defines the contract; Phase 2 implements PublishAsync.
/// </summary>
public interface ISolutionPublishService
{
    /// <summary>Publishes all or selective customizations.</summary>
    Task PublishAsync(string? profileName, IReadOnlyList<string>? entityLogicalNames, CancellationToken ct);
}
```

> **Note:** Delete is handled by the existing `ISolutionUninstallService` — no separate `ISolutionDeleteService` is needed.

### 4.2 `ISolutionComponentQueryService.cs`

> **SOLID fix (S2/I2):** The original `ISolutionComponentService` mixed read-only queries (Web API virtual entities) and mutative operations (SDK typed messages). `[CliReadOnly]` commands were forced to depend on destructive `RemoveAsync`. Split into query (read) and mutation (write) interfaces. See [SOLID audit report](./research/solid-audit-report.md) findings S2, I2.

**File to create:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionComponentQueryService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>A single solution component summary row.</summary>
public sealed record ComponentSummaryRow(
    string Type,
    string? DisplayName,
    string? Name,
    Guid ObjectId,
    bool Managed,
    bool Customizable);

/// <summary>
/// Read-only component queries within a specific solution (Web API virtual entities).
/// Phase 0 defines the contract; Phase 1 implements CountAsync / ListAsync.
/// </summary>
public interface ISolutionComponentQueryService
{
    /// <summary>
    /// Returns component type counts for a solution. Requires the solution GUID —
    /// callers must resolve the unique name to a GUID first (via <see cref="ISolutionDetailService"/>
    /// or a direct query).
    /// </summary>
    Task<IReadOnlyList<ComponentCountRow>> CountAsync(
        string? profileName, Guid solutionId, CancellationToken ct);

    /// <summary>Lists components in a solution with optional filtering.</summary>
    Task<IReadOnlyList<ComponentSummaryRow>> ListAsync(
        string? profileName, Guid solutionId, int? componentTypeFilter, int? top, CancellationToken ct);
}
```

### 4.2b `ISolutionComponentMutationService.cs`

**File to create:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionComponentMutationService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Mutative operations on solution components (SDK typed messages).
/// Phase 0 defines the contract; Phase 3 implements AddAsync / RemoveAsync.
/// </summary>
public interface ISolutionComponentMutationService
{
    /// <summary>Adds an existing component to an unmanaged solution.</summary>
    Task<ComponentAddOutcome> AddAsync(
        string? profileName, ComponentAddOptions options, CancellationToken ct);

    /// <summary>Removes a component from an unmanaged solution.</summary>
    Task<ComponentRemoveOutcome> RemoveAsync(
        string? profileName, ComponentRemoveOptions options, CancellationToken ct);
}
```

### 4.3 `ISolutionDependencyService.cs`

> **SOLID fix (I3):** Define the interface incrementally — PR 1 only defines `CheckUninstallAsync`. The other 3 methods (`GetDependentsAsync`, `GetRequiredAsync`, `CheckDeleteAsync`) are added to the interface when PR 3 (component dependencies) is implemented. This avoids shipping `NotImplementedException` stubs. See [SOLID audit report](./research/solid-audit-report.md) finding I3.

**File to create:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionDependencyService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// A single dependency row returned by dependency check APIs.
/// Canonical definition — matches Phase 1 implementation.
/// </summary>
public sealed record DependencyRow(
    Guid DependentComponentId,
    int DependentComponentType,
    string? DependentComponentName,
    Guid RequiredComponentId,
    int RequiredComponentType,
    string? RequiredComponentName,
    int DependencyType);

/// <summary>
/// Dependency analysis for solution components and solutions.
/// All methods are read-only.
/// PR 1 defines only CheckUninstallAsync; PR 3 extends the interface
/// with GetDependentsAsync, GetRequiredAsync, and CheckDeleteAsync.
/// </summary>
public interface ISolutionDependencyService
{
    /// <summary>Check what blocks uninstalling a solution.</summary>
    Task<IReadOnlyList<DependencyRow>> CheckUninstallAsync(
        string? profileName, string solutionUniqueName, CancellationToken ct);

    // PR 3 adds:
    // Task<IReadOnlyList<DependencyRow>> GetDependentsAsync(...);
    // Task<IReadOnlyList<DependencyRow>> GetRequiredAsync(...);
    // Task<IReadOnlyList<DependencyRow>> CheckDeleteAsync(...);
}
```

### 4.4 `ISolutionLayerQueryService.cs`

> **SOLID fix (L1):** The original `ISolutionLayerService` mixed read-only layer queries with the destructive `RemoveCustomizationAsync`. `[CliReadOnly]` commands (`ComponentLayerListCliCommand`, `ComponentLayerShowCliCommand`) were forced to depend on a mutation method. Split into query and mutation interfaces. See [SOLID audit report](./research/solid-audit-report.md) finding L1.

**File to create:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionLayerQueryService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// A single solution layer in the layer stack for a component.
/// Canonical definition — matches Phase 1 implementation.
/// </summary>
public sealed record ComponentLayerRow(
    int Order,
    string SolutionName,
    string? PublisherName,
    string? Name,
    DateTime OverwriteTime,
    string? ComponentJson,
    string? Changes);

/// <summary>
/// Read-only solution-layer inspection.
/// Phase 0 defines the contract; Phase 1 implements ListLayersAsync / GetActiveLayerJsonAsync.
/// Note: Layer APIs use string parameters (not Guid/int) because the underlying
/// Web API OData filters use string representations of component IDs and type names.
/// </summary>
public interface ISolutionLayerQueryService
{
    /// <summary>Returns the full solution layer stack for a component.</summary>
    Task<IReadOnlyList<ComponentLayerRow>> ListLayersAsync(
        string? profileName, string componentId, string componentTypeName, CancellationToken ct);

    /// <summary>Returns the active layer's component definition JSON.</summary>
    Task<string?> GetActiveLayerJsonAsync(
        string? profileName, string componentId, string componentTypeName, CancellationToken ct);
}
```

### 4.4b `ISolutionLayerMutationService.cs`

**File to create:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionLayerMutationService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Destructive solution-layer operations.
/// Phase 0 defines the contract; Phase 3 implements RemoveCustomizationAsync.
/// </summary>
public interface ISolutionLayerMutationService
{
    /// <summary>Removes the unmanaged (active) customization layer from a component.</summary>
    Task RemoveCustomizationAsync(
        string? profileName, Guid componentId, int componentType, CancellationToken ct);
}
```

### Implementation notes for all contracts

- Every method takes `string? profileName` as first parameter — this is the `txc` profile pattern (see `ISolutionImportService`). `null` means "use the default profile".
- Every method takes `CancellationToken ct` as last parameter.
- DTOs are `sealed record` types — immutable, value-equality, concise.
- `ComponentCountRow` is shared between `ISolutionDetailService` and `ISolutionComponentQueryService` — define it **once** in `ISolutionDetailService.cs` (or extract to a shared file if preferred).
- **Two-layer service pattern:** Each service should follow the existing `SolutionUninstaller`/`DataverseSolutionUninstallService` pattern: a thin Service class in `Services/` that connects via `DataverseCommandBridge.ConnectAsync()` and delegates to an SDK helper class in `Sdk/` that receives `IOrganizationServiceAsync2`. This keeps SDK logic unit-testable without mocking `DataverseCommandBridge`.
- **HTTP status checking:** When using `ExecuteWebRequest`, always call `response.EnsureSuccessStatusCode()` (or check `response.IsSuccessStatusCode`) before parsing the response body. This prevents confusing `JsonException` errors when the server returns 4xx/5xx.

### How to verify

- Build the `TALXIS.CLI.Core` project: `dotnet build src/TALXIS.CLI.Core/`.
- Interfaces compile = done. No runtime test needed at this stage.

---

## 5. SolutionPackager Integration

SolutionPackagerLib is the library that packs/unpacks Dataverse solution ZIPs into human-readable file trees. Required for `comp export` (unpack exported ZIP) and for the future workspace sync feature.

### Integration approach: Direct NuGet reference via platform-specific PAC CLI Core packages

SolutionPackagerLib.dll ships inside the `Microsoft.PowerApps.CLI.Core.{rid}` NuGet packages. We reference the DLL at build time — **no PAC CLI prerequisite for users**.

### 5.1 File to modify — `.csproj`

`src/TALXIS.CLI.Platform.Dataverse.Application/TALXIS.CLI.Platform.Dataverse.Application.csproj`

Add inside an `<ItemGroup>`:

```xml
<!-- PAC CLI Core — only needed for SolutionPackagerLib.dll (managed, AnyCPU) -->
<PackageReference Include="Microsoft.PowerApps.CLI.Core.linux-x64" Version="2.6.4"
                  GeneratePathProperty="true" ExcludeAssets="all" />

<!-- Direct assembly reference (identical DLL across all RID packages) -->
<Reference Include="SolutionPackagerLib">
  <HintPath>$(PkgMicrosoft_PowerApps_CLI_Core_linux-x64)\tools\SolutionPackagerLib.dll</HintPath>
  <Private>true</Private>
</Reference>
```

**Verified facts:**
- `SolutionPackagerLib.dll` exists at `$(PkgMicrosoft_PowerApps_CLI_Core_osx-x64)/tools/SolutionPackagerLib.dll`
- It's a **managed .NET assembly (AnyCPU)** — platform-independent, runs on Windows/macOS/Linux
- **Identical DLL** across all RID packages (SHA `a5d31a1d...` same for osx-x64 and linux-x64)
- We only need to reference from one RID package at build time; the assembly works everywhere

**Dependencies** (also in the same `tools/` folder, also managed): Newtonsoft.Json, YamlDotNet, MEF, ConfigurationManager, Compression.Cab, IO.Packaging.

**Cross-platform:** No RID-conditional logic needed. Reference from any one platform package (e.g. `linux-x64`) since the managed DLL is identical across all.

### 5.2 File to create — Service wrapper

`src/TALXIS.CLI.Platform.Dataverse.Application/Sdk/SolutionPackagerService.cs`

```csharp
using Microsoft.Extensions.Logging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Sdk;

/// <summary>
/// Wraps SolutionPackagerLib to extract (unpack) and pack solution ZIPs.
/// This is a thin adapter — the heavy lifting is done by the PAC CLI's
/// SolutionPackager class inside SolutionPackagerLib.dll.
/// </summary>
public sealed class SolutionPackagerService
{
    private readonly ILogger? _logger;

    public SolutionPackagerService(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts (unpacks) a solution ZIP into a folder of human-readable files.
    /// </summary>
    /// <param name="solutionZipPath">Path to the .zip file.</param>
    /// <param name="outputFolder">Target folder for unpacked files.</param>
    /// <param name="packageType">Unmanaged, Managed, or Both.</param>
    public void Extract(string solutionZipPath, string outputFolder, string packageType = "Unmanaged")
    {
        // Minimal usage:
        //   var arguments = new PackagerArguments
        //   {
        //       Action = CommandAction.Extract,
        //       PathToZipFile = solutionZipPath,
        //       Folder = outputFolder,
        //       PackageType = Enum.Parse<SolutionPackageType>(packageType),
        //       AllowDeletes = AllowDelete.Yes,
        //       AllowWrites = AllowWrite.Yes,
        //   };
        //   new SolutionPackager(arguments).Run();
        throw new NotImplementedException();
    }

    /// <summary>
    /// Packs an unpacked solution folder into a solution ZIP.
    /// </summary>
    /// <param name="sourceFolder">Folder containing unpacked solution files.</param>
    /// <param name="outputZipPath">Path for the output .zip file.</param>
    /// <param name="packageType">Unmanaged, Managed, or Both.</param>
    public void Pack(string sourceFolder, string outputZipPath, string packageType = "Unmanaged")
    {
        // Same as Extract but with Action = CommandAction.Pack
        // and PathToZipFile / Folder swapped semantically.
        throw new NotImplementedException();
    }
}
```

### Implementation notes

- Do **not** register `SolutionPackagerService` in DI — it has no interface contract in `Core`. It's instantiated directly by SDK classes that need it (similar to `ComponentTypeResolver`).
- After adding the NuGet reference, run `dotnet restore` and verify `SolutionPackagerLib` is resolvable.
- The `SolutionPackager` and `PackagerArguments` types come from `SolutionPackagerLib.dll`. Check the exact namespace after restore (likely `Microsoft.Crm.Tools.SolutionPackager`).

Full analysis: [research/solution-packager-analysis.md](./research/solution-packager-analysis.md)

### How to verify

- `dotnet build` succeeds after adding the NuGet reference.
- Write a small integration test that unpacks a known solution ZIP into a temp folder and verifies the `solution.xml` file appears. Use a test fixture ZIP committed to the test project.

---

## 6. DI Registration

### File to modify

`src/TALXIS.CLI.Platform.Dataverse.Application/DependencyInjection/DataverseApplicationServiceCollectionExtensions.cs`

### Changes

Add registrations for the new service interfaces. Create **stub implementations** (throw `NotImplementedException`) so the project compiles end-to-end. The stubs will be replaced with real logic in Phases 1–3.

Add these lines inside `AddTxcDataverseApplication()`, after the existing registrations:

```csharp
// ── Solution Management (Phase 0 stubs — replaced in Phases 1–3) ──
services.AddSingleton<ISolutionDetailService, DataverseSolutionDetailService>();
services.AddSingleton<ISolutionCreateService, DataverseSolutionCreateService>();
services.AddSingleton<ISolutionPublishService, DataverseSolutionPublishService>();
services.AddSingleton<ISolutionComponentQueryService, DataverseSolutionComponentQueryService>();
services.AddSingleton<ISolutionComponentMutationService, DataverseSolutionComponentMutationService>();
services.AddSingleton<ISolutionDependencyService, DataverseSolutionDependencyService>();
services.AddSingleton<ISolutionLayerQueryService, DataverseSolutionLayerQueryService>();
services.AddSingleton<ISolutionLayerMutationService, DataverseSolutionLayerMutationService>();
```

### Stub service files to create

Create stub files in `src/TALXIS.CLI.Platform.Dataverse.Application/Services/`:

| File | Class | Implements |
|------|-------|------------|
| `DataverseSolutionDetailService.cs` | `DataverseSolutionDetailService` | `ISolutionDetailService` |
| `DataverseSolutionCreateService.cs` | `DataverseSolutionCreateService` | `ISolutionCreateService` |
| `DataverseSolutionPublishService.cs` | `DataverseSolutionPublishService` | `ISolutionPublishService` |
| `DataverseSolutionComponentQueryService.cs` | `DataverseSolutionComponentQueryService` | `ISolutionComponentQueryService` |
| `DataverseSolutionComponentMutationService.cs` | `DataverseSolutionComponentMutationService` | `ISolutionComponentMutationService` |
| `DataverseSolutionDependencyService.cs` | `DataverseSolutionDependencyService` | `ISolutionDependencyService` |
| `DataverseSolutionLayerQueryService.cs` | `DataverseSolutionLayerQueryService` | `ISolutionLayerQueryService` |
| `DataverseSolutionLayerMutationService.cs` | `DataverseSolutionLayerMutationService` | `ISolutionLayerMutationService` |

Each stub follows the same pattern as `DataverseSolutionImportService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionDetailService : ISolutionDetailService
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataverseSolutionDetailService));

    public Task<(SolutionDetail Solution, IReadOnlyList<ComponentCountRow> Counts)> GetAsync(
        string? profileName, string solutionUniqueName, CancellationToken ct)
    {
        // Phase 1 implementation:
        //   using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        //   // Delegate to SolutionDetailReader SDK class (SOLID fix S4).
        //   // SolutionDetailReader queries solution table + msdyn_solutioncomponentcountsummaries.
        //   // Use shared ComponentCountReader for the count query (SOLID fix DRY1).
        throw new NotImplementedException("Implemented in Phase 1.");
    }
}
```

> **SOLID fix (S4):** Service classes should follow the two-layer pattern — delegate to SDK helper classes. `DataverseSolutionDetailService` delegates to a `SolutionDetailReader` SDK class (in `Sdk/SolutionDetailReader.cs`), following the `SolutionUninstaller`/`DataverseSolutionUninstallService` pattern. The service class connects via `DataverseCommandBridge.ConnectAsync()` and passes the connection to the SDK class. This keeps SDK logic unit-testable.

> **SOLID fix (DRY1):** Extract the `msdyn_solutioncomponentcountsummaries` query logic into a shared `ComponentCountReader` SDK helper class (in `Sdk/ComponentCountReader.cs`). Both `DataverseSolutionDetailService.GetAsync` and `DataverseSolutionComponentQueryService.CountAsync` delegate to this shared reader, eliminating duplicated OData filter logic and JSON parsing.

Repeat the stub pattern for the other service files — each method body is `throw new NotImplementedException("Implemented in Phase N.");` with a comment describing the implementation strategy.

### How to verify

- `dotnet build` the full solution: `dotnet build TALXIS.CLI.sln`.
- The app should start and existing commands (`txc env sln list`, etc.) should still work — the new services are registered but not yet called by any command.

---

## Key Decision: SDK vs Web API

| Operation | Approach | Reason |
|-----------|----------|--------|
| Solution CRUD | SDK (`ExecuteAsync`) | Typed messages available |
| Component add/remove | SDK (`AddSolutionComponentRequest`) | Typed messages available |
| Dependency queries | `ExecuteWebRequest` (Web API) | Web API functions are simpler and avoid SDK message version dependencies |
| Layer remove customization | SDK (`RemoveActiveCustomizationsRequest`) | Typed message available |
| Component list/count | `ExecuteWebRequest` (Web API) | Virtual entities only queryable via OData |
| Layer queries | `ExecuteWebRequest` (Web API) | `msdyn_componentlayer` is a virtual entity |
| Component count summaries | `ExecuteWebRequest` (Web API) | `msdyn_solutioncomponentcountsummaries` virtual entity |

All Web API calls go through `ServiceClient.ExecuteWebRequest()` — no separate HTTP client.

---

## PR Scope

### What goes IN this PR

- [ ] Schema constants in `DataverseSchema.cs` (section 1)
- [ ] `ComponentTypeResolver.cs` with full platform-type mapping + SCF loading skeleton (section 2)
- [ ] `ComponentNameResolver.cs` with resolution strategy skeleton (section 3)
- [ ] Seven service contract files in `Core/Contracts/Dataverse/` (section 4): `ISolutionDetailService`, `ISolutionCreateService`, `ISolutionPublishService`, `ISolutionComponentQueryService`, `ISolutionComponentMutationService`, `ISolutionDependencyService`, `ISolutionLayerQueryService`, `ISolutionLayerMutationService`
- [ ] SolutionPackager NuGet reference in `.csproj` (section 5.1)
- [ ] `SolutionPackagerService.cs` wrapper skeleton (section 5.2)
- [ ] Eight stub service implementations in `Services/` (section 6)
- [ ] DI registration additions (section 6)
- [ ] Unit tests for `ComponentTypeResolver` platform-type mapping
- [ ] Full solution builds with `dotnet build TALXIS.CLI.sln`

### What does NOT go in this PR

- No CLI commands (those are Phase 1+)
- No real service implementations (stubs only — `throw new NotImplementedException`)
- No `ComponentNameResolver` integration tests (needs live environment)
- No SolutionPackager runtime usage (just the reference + wrapper skeleton)

### PR checklist

1. `dotnet build TALXIS.CLI.sln` passes
2. Existing tests pass (`dotnet test`)
3. Existing commands (`txc env sln list`, `txc env sln import`, etc.) unaffected
4. New files follow existing code style (namespace conventions, `internal sealed class`, `ILogger` via `TxcLoggerFactory`)

---

## Testing Approach

| What | Test Type | Where | When |
|------|-----------|-------|------|
| Platform type mapping (all 16 entries) | Unit | `tests/…/ComponentTypeResolverTests.cs` | Phase 0 PR |
| `Resolve()` / `ResolveByName()` round-trip | Unit | `tests/…/ComponentTypeResolverTests.cs` | Phase 0 PR |
| `GetDisplayName()` for known + unknown codes | Unit | `tests/…/ComponentTypeResolverTests.cs` | Phase 0 PR |
| `ComponentNameResolver` cache behavior | Unit (mocked service) | `tests/…/ComponentNameResolverTests.cs` | Phase 0 PR |
| `SolutionPackagerService.Extract()` | Integration (test ZIP) | `tests/…/SolutionPackagerServiceTests.cs` | Phase 0 PR (if test ZIP available) |
| Full solution build | Build | CI | Phase 0 PR |
| Existing commands unbroken | Manual | Local | Phase 0 PR |
| `LoadScfTypesAsync()` against live env | Integration (manual) | Local only | Phase 1 (when first command uses it) |
| Service contract DTOs serialization | Unit (optional) | `tests/…/` | Phase 1+ |
