# Phase 1 — Read-Only Inspection Commands

All APIs verified working ✅ against `org2928f636.crm.dynamics.com`.

Depends on: [Phase 0 — Infrastructure](./00-infrastructure.md)

---

## PR Strategy

Split Phase 1 into four pull requests. Each PR is shippable independently and adds user-facing value.

| PR | Commands | Why grouped |
|----|----------|-------------|
| **PR 1** | `sln show`, `sln component count`, `sln component list`, `sln uninstall-check` | Core solution inspection; shares `ISolutionComponentQueryService` and `ISolutionDependencyService` interfaces |
| **PR 2** | `comp layer list`, `comp layer show` | Layer APIs share `ISolutionLayerQueryService` and the `msdyn_componentlayers` Web API |
| **PR 3** | `comp dep list`, `comp dep required`, `comp dep delete-check` | Dependency APIs share `ISolutionDependencyService` (partial — added in PR 1 for `uninstall-check`) and `ComponentNameResolver` |
| **PR 4** | `comp export` | Complex; depends on SolutionPackager integration from Phase 0 infrastructure |

**Implementation order within each PR:** implement service interface + DTO first, then the service implementation, then the CLI command, then register in DI, then update the group command's `Children`. Build & test after each command.

---

## Architecture Recap (refer to existing code for patterns)

Every command follows this layered architecture:

```
CLI Command (Features.Environment)
    → calls service interface (Core/Contracts/Dataverse/I*Service.cs)
        → implemented by (Platform.Dataverse.Application/Services/Dataverse*Service.cs)
            → which uses DataverseCommandBridge.ConnectAsync() to get a connection
            → delegates to SDK helper class (Platform.Dataverse.Application/Sdk/*.cs)
```

> **Two-layer service pattern:** Each service should follow the existing `SolutionUninstaller`/`DataverseSolutionUninstallService` pattern: a thin Service class in `Services/` connects via `DataverseCommandBridge.ConnectAsync()` and delegates to an SDK helper class in `Sdk/` that receives `IOrganizationServiceAsync2`. This keeps SDK logic unit-testable. For Web API-only operations (virtual entity queries), the service class may contain the HTTP/JSON logic directly since there's no SDK equivalent to extract.

**Key files to reference for patterns:**
- CLI command: `SolutionListCliCommand.cs` — `[CliReadOnly]`, extends `ProfiledCliCommand`, uses `TxcServices.Get<T>()`, calls `OutputFormatter.WriteList()`
- Service interface + DTO record: `ISolutionInventoryService.cs` — interface + record in one file under `Core/Contracts/Dataverse/`
- Service implementation: `DataverseSolutionInventoryService.cs` — thin wrapper calling `DataverseCommandBridge.ConnectAsync()`
- Group command: `SolutionCliCommand.cs` — `Children` array wiring
- DI registration: `DataverseApplicationServiceCollectionExtensions.cs` — `AddSingleton<IFoo, Foo>()`

---

## PR 1 — Solution Show, Component Count/List, Uninstall-Check

### New Group Commands (scaffolding)

Before creating any leaf commands, create the group commands that establish the command tree. These are empty parent nodes that show help.

#### 1a. `SolutionComponentCliCommand` (group: `txc env sln component`)

**File:** `src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentCliCommand.cs`

```csharp
using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Solution.Component;

[CliCommand(
    Name = "component",
    Description = "Inspect components within a solution.",
    Children = new[]
    {
        typeof(SolutionComponentListCliCommand),
        typeof(SolutionComponentCountCliCommand),
    }
)]
public class SolutionComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
```

> **After PR 2/3/4:** You will NOT add layer/dependency/export commands here. Those live under `env component` (solution-independent), not `env sln component`. See the command tree in `README.md`.

#### 1b. Update `SolutionCliCommand.Children`

**File:** `src/TALXIS.CLI.Features.Environment/Solution/SolutionCliCommand.cs`

Add `typeof(SolutionShowCliCommand)`, `typeof(SolutionUninstallCheckCliCommand)`, and `typeof(Component.SolutionComponentCliCommand)` to the `Children` array:

```csharp
[CliCommand(
    Name = "solution",
    Alias = "sln",
    Description = "Manage solutions in the target environment.",
    Children = new[]
    {
        typeof(SolutionImportCliCommand),
        typeof(SolutionUninstallCliCommand),
        typeof(SolutionListCliCommand),
        typeof(SolutionShowCliCommand),
        typeof(SolutionUninstallCheckCliCommand),
        typeof(Component.SolutionComponentCliCommand),
    }
)]
```

---

### Command 1: `txc env sln show <name>`

Show solution details (name, version, publisher, install date) plus a component type count breakdown.

#### Step 1 — Service contract + DTOs

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionDetailService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Detailed solution info returned by <see cref="ISolutionDetailService.GetAsync"/>.
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
/// </summary>
public sealed record ComponentCountRow(
    string TypeName,
    int TypeCode,
    string? LogicalName,
    int Count);

public interface ISolutionDetailService
{
    /// <summary>
    /// Retrieves solution details by unique name, including publisher info
    /// and component type count breakdown.
    /// </summary>
    Task<(SolutionDetail Solution, IReadOnlyList<ComponentCountRow> Counts)> GetAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct);
}
```

#### Step 2 — Service implementation

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseSolutionDetailService.cs`

> **SOLID note (S4):** In the final implementation, this service should delegate to a `SolutionDetailReader` SDK class rather than containing all logic inline. The `SolutionDetailReader` handles SDK query + Web API call + JSON parsing. The count query should use the shared `ComponentCountReader` SDK helper (DRY1). See Phase 0 §6 notes.

```csharp
using System.Net.Http;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionDetailService : ISolutionDetailService
{
    public async Task<(SolutionDetail Solution, IReadOnlyList<ComponentCountRow> Counts)> GetAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // 1. Query the solution table with publisher expand (SDK QueryExpression).
        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet(
                "solutionid", "uniquename", "friendlyname", "version",
                "ismanaged", "installedon", "description"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, solutionUniqueName);

        var publisherLink = query.AddLink("publisher", "publisherid", "publisherid", JoinOperator.LeftOuter);
        publisherLink.EntityAlias = "pub";
        publisherLink.Columns = new ColumnSet("uniquename", "friendlyname", "customizationprefix");

        var result = await conn.Client.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (result.Entities.Count == 0)
            throw new InvalidOperationException($"Solution '{solutionUniqueName}' not found.");

        var entity = result.Entities[0];
        var detail = new SolutionDetail(
            Id: entity.Id,
            UniqueName: entity.GetAttributeValue<string>("uniquename") ?? solutionUniqueName,
            FriendlyName: entity.GetAttributeValue<string>("friendlyname"),
            Version: entity.GetAttributeValue<string>("version"),
            Managed: entity.GetAttributeValue<bool>("ismanaged"),
            InstalledOn: entity.GetAttributeValue<DateTime?>("installedon"),
            Description: entity.GetAttributeValue<string>("description"),
            PublisherName: entity.GetAttributeValue<AliasedValue>("pub.friendlyname")?.Value as string,
            PublisherPrefix: entity.GetAttributeValue<AliasedValue>("pub.customizationprefix")?.Value as string);

        // 2. Get component count breakdown (Web API virtual entity).
        var countPath = $"msdyn_solutioncomponentcountsummaries?$filter=msdyn_solutionid eq {detail.Id}";
        var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, countPath, string.Empty);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        var counts = new List<ComponentCountRow>();
        if (doc.RootElement.TryGetProperty("value", out var valueArray))
        {
            foreach (var item in valueArray.EnumerateArray())
            {
                counts.Add(new ComponentCountRow(
                    // msdyn_componentlogicalname is the friendly type name (e.g., "entity")
                    TypeName: item.GetProperty("msdyn_componentlogicalname").GetString() ?? "(unknown)",
                    TypeCode: item.GetProperty("msdyn_componenttype").GetInt32(),
                    LogicalName: item.TryGetProperty("msdyn_componentlogicalname", out var ln) ? ln.GetString() : null,
                    Count: item.GetProperty("msdyn_total").GetInt32()));
            }
        }

        return (detail, counts);
    }
}
```

**Implementation notes:**
- `ExecuteWebRequest` returns `HttpResponseMessage`. Parse JSON from `response.Content`.
- The filter `msdyn_solutionid eq {guid}` works (tested), but the field itself is null in response rows — ignore it.
- No custom headers needed for this call (no `Prefer` annotation header).

#### Step 3 — CLI command

**File:** `src/TALXIS.CLI.Features.Environment/Solution/SolutionShowCliCommand.cs`

```csharp
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliReadOnly]
[CliCommand(
    Name = "show",
    Description = "Show solution details and component type breakdown."
)]
public class SolutionShowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionShowCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.", Required = true)]
    public required string Name { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("'name' argument is required.");
            return ExitValidationError;
        }

        var service = TxcServices.Get<ISolutionDetailService>();
        var (solution, counts) = await service.GetAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        // Use OutputFormatter.WriteData for the solution detail + WriteList for counts.
        OutputFormatter.WriteData(solution, s => PrintSolutionDetail(s, counts));
        return ExitSuccess;
    }

    // Text-renderer callback invoked by OutputFormatter.WriteData — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintSolutionDetail(SolutionDetail s, IReadOnlyList<ComponentCountRow> counts)
    {
        OutputWriter.WriteLine($"Unique Name:  {s.UniqueName}");
        OutputWriter.WriteLine($"Display Name: {s.FriendlyName ?? "(none)"}");
        OutputWriter.WriteLine($"Version:      {s.Version ?? "(unknown)"}");
        OutputWriter.WriteLine($"Managed:      {s.Managed}");
        OutputWriter.WriteLine($"Installed On: {s.InstalledOn?.ToString("u") ?? "(unknown)"}");
        OutputWriter.WriteLine($"Publisher:    {s.PublisherName ?? "(unknown)"} (prefix: {s.PublisherPrefix ?? "none"})");
        if (!string.IsNullOrWhiteSpace(s.Description))
            OutputWriter.WriteLine($"Description:  {s.Description}");

        if (counts.Count > 0)
        {
            OutputWriter.WriteLine();
            OutputWriter.WriteLine("Component Counts:");
            int nameWidth = Math.Clamp(counts.Max(c => c.TypeName.Length), 10, 40);
            string header = $"  {"Type".PadRight(nameWidth)} | {"Code",5} | Count";
            OutputWriter.WriteLine(header);
            OutputWriter.WriteLine("  " + new string('-', header.Length - 2));
            foreach (var c in counts.OrderByDescending(c => c.Count))
            {
                OutputWriter.WriteLine($"  {c.TypeName.PadRight(nameWidth)} | {c.TypeCode,5} | {c.Count}");
            }
        }
    }
#pragma warning restore TXC003
}
```

**Notes for the implementer:**
- The `OutputFormatter.WriteData<T>(data, textRenderer)` auto-serializes to JSON when `--format json` is used. You only write the human-readable text renderer.
- `#pragma warning disable TXC003` is required around methods that call `OutputWriter.WriteLine` directly — the analyzer flags these unless they are text-renderer callbacks passed to `OutputFormatter`. See `SolutionListCliCommand.cs` for the pattern.

#### Step 4 — DI registration

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/DependencyInjection/DataverseApplicationServiceCollectionExtensions.cs`

Add this line inside `AddTxcDataverseApplication()`:

```csharp
services.AddSingleton<ISolutionDetailService, DataverseSolutionDetailService>();
```

---

### Command 2: `txc env sln component count <solution>`

Quick per-type component counts for a solution. Simpler than `sln show` — shows only the count table.

#### Step 1 — Service contract

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionComponentQueryService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// Row returned by <see cref="ISolutionComponentQueryService.ListAsync"/>.
/// </summary>
public sealed record ComponentSummaryRow(
    string Type,
    string? DisplayName,
    string? Name,
    Guid ObjectId,
    bool Managed,
    bool Customizable);

public interface ISolutionComponentQueryService
{
    /// <summary>
    /// Returns component type counts for a solution. Requires the solution GUID —
    /// callers must resolve the unique name to a GUID first (via <see cref="ISolutionDetailService"/>
    /// or a direct query).
    /// </summary>
    Task<IReadOnlyList<ComponentCountRow>> CountAsync(
        string? profileName,
        Guid solutionId,
        CancellationToken ct);

    /// <summary>
    /// Lists components in a solution with optional filtering.
    /// </summary>
    Task<IReadOnlyList<ComponentSummaryRow>> ListAsync(
        string? profileName,
        Guid solutionId,
        int? componentTypeFilter,
        int? top,
        CancellationToken ct);
}
```

> `ComponentCountRow` is already defined in `ISolutionDetailService.cs` above. Reuse it — do not duplicate.

#### Step 2 — Service implementation

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseSolutionComponentQueryService.cs`

> **SOLID note (DRY1):** The `CountAsync` method should delegate to the shared `ComponentCountReader` SDK helper class rather than containing the OData query inline. This eliminates duplication with `DataverseSolutionDetailService.GetAsync`.

```csharp
using System.Net.Http;
using System.Text.Json;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionComponentQueryService : ISolutionComponentQueryService
{
    public async Task<IReadOnlyList<ComponentCountRow>> CountAsync(
        string? profileName,
        Guid solutionId,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var path = $"msdyn_solutioncomponentcountsummaries?$filter=msdyn_solutionid eq {solutionId}";
        var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        var counts = new List<ComponentCountRow>();
        if (doc.RootElement.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                counts.Add(new ComponentCountRow(
                    TypeName: item.GetProperty("msdyn_componentlogicalname").GetString() ?? "(unknown)",
                    TypeCode: item.GetProperty("msdyn_componenttype").GetInt32(),
                    LogicalName: item.TryGetProperty("msdyn_componentlogicalname", out var ln) ? ln.GetString() : null,
                    Count: item.GetProperty("msdyn_total").GetInt32()));
            }
        }

        return counts;
    }

    public async Task<IReadOnlyList<ComponentSummaryRow>> ListAsync(
        string? profileName,
        Guid solutionId,
        int? componentTypeFilter,
        int? top,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Build OData filter. Boolean filters (msdyn_ismanaged) cause BadRequest — filter client-side.
        var filter = $"msdyn_solutionid eq {solutionId}";
        if (componentTypeFilter.HasValue)
            filter += $" and (msdyn_componenttype eq {componentTypeFilter.Value})";

        var select = "msdyn_componenttypename,msdyn_displayname,msdyn_name,msdyn_objectid,msdyn_ismanaged,msdyn_iscustomizable";
        var path = $"msdyn_solutioncomponentsummaries?$filter={filter}&$select={select}";
        if (top.HasValue)
            path += $"&$top={top.Value}";

        var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        var rows = new List<ComponentSummaryRow>();
        if (doc.RootElement.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                rows.Add(new ComponentSummaryRow(
                    Type: item.GetProperty("msdyn_componenttypename").GetString() ?? "(unknown)",
                    DisplayName: item.TryGetProperty("msdyn_displayname", out var dn) ? dn.GetString() : null,
                    Name: item.TryGetProperty("msdyn_name", out var n) ? n.GetString() : null,
                    ObjectId: Guid.Parse(item.GetProperty("msdyn_objectid").GetString()!),
                    Managed: item.TryGetProperty("msdyn_ismanaged", out var m) && m.GetBoolean(),
                    Customizable: item.TryGetProperty("msdyn_iscustomizable", out var c) && c.GetBoolean()));
            }
        }

        // Handle @odata.nextLink pagination if top is not set.
        // Follow the pattern from DataverseQueryService.FollowNextLinksAsync.
        // TODO: extract pagination into a shared helper in a follow-up.

        return rows;
    }
}
```

**Important quirk — resolving solution unique name to GUID:**

The `msdyn_solutioncomponentcountsummaries` and `msdyn_solutioncomponentsummaries` virtual entities filter by `msdyn_solutionid` (GUID), not by unique name. Both `CountAsync` and `ListAsync` take a `Guid solutionId`. The CLI command must resolve the name to a GUID first. Two options:
1. Call `ISolutionDetailService.GetAsync()` which returns `SolutionDetail.Id` (simple, one extra call).
2. Add a lightweight `ResolveIdAsync(profileName, uniqueName)` method to `ISolutionDetailService`.

For PR 1, use option 1 — call `GetAsync` and use `solution.Id`. Optimize later if needed.

#### Step 3 — CLI command for `component count`

**File:** `src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentCountCliCommand.cs`

```csharp
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution.Component;

[CliReadOnly]
[CliCommand(
    Name = "count",
    Description = "Show per-component-type counts for a solution."
)]
public class SolutionComponentCountCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionComponentCountCliCommand));

    [CliArgument(Name = "solution", Description = "Solution unique name.", Required = true)]
    public required string Solution { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Solution))
        {
            Logger.LogError("'solution' argument is required.");
            return ExitValidationError;
        }

        // Resolve solution unique name → GUID
        var detailService = TxcServices.Get<ISolutionDetailService>();
        var (detail, _) = await detailService.GetAsync(Profile, Solution, CancellationToken.None).ConfigureAwait(false);

        var compService = TxcServices.Get<ISolutionComponentQueryService>();
        var counts = await compService.CountAsync(Profile, detail.Id, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(counts, PrintCountsTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintCountsTable(IReadOnlyList<ComponentCountRow> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No components found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.TypeName.Length), 10, 40);
        string header = $"{"Type".PadRight(nameWidth)} | {"Code",5} | Count";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows.OrderByDescending(r => r.Count))
        {
            OutputWriter.WriteLine($"{r.TypeName.PadRight(nameWidth)} | {r.TypeCode,5} | {r.Count}");
        }
    }
#pragma warning restore TXC003
}
```

#### Step 4 — CLI command for `component list`

**File:** `src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentListCliCommand.cs`

```csharp
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution.Component;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List components in a solution."
)]
public class SolutionComponentListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionComponentListCliCommand));

    [CliArgument(Name = "solution", Description = "Solution unique name.", Required = true)]
    public required string Solution { get; set; }

    [CliOption(Name = "--type", Description = "Filter by component type code (integer).", Required = false)]
    public int? Type { get; set; }

    [CliOption(Name = "--top", Description = "Limit the number of results.", Required = false)]
    public int? Top { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Solution))
        {
            Logger.LogError("'solution' argument is required.");
            return ExitValidationError;
        }

        // Resolve solution unique name → GUID
        var detailService = TxcServices.Get<ISolutionDetailService>();
        var (detail, _) = await detailService.GetAsync(Profile, Solution, CancellationToken.None).ConfigureAwait(false);

        var compService = TxcServices.Get<ISolutionComponentQueryService>();
        var rows = await compService.ListAsync(Profile, detail.Id, Type, Top, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintComponentsTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintComponentsTable(IReadOnlyList<ComponentSummaryRow> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No components found.");
            return;
        }

        int typeWidth = Math.Clamp(rows.Max(r => r.Type.Length), 4, 30);
        int displayWidth = Math.Clamp(rows.Max(r => (r.DisplayName ?? "").Length), 12, 40);
        int nameWidth = Math.Clamp(rows.Max(r => (r.Name ?? "").Length), 4, 40);
        int idWidth = 36; // GUID
        int managedWidth = 7;
        int custWidth = 12;

        string header =
            $"{"Type".PadRight(typeWidth)} | " +
            $"{"Display Name".PadRight(displayWidth)} | " +
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"ObjectId".PadRight(idWidth)} | " +
            $"{"Managed".PadRight(managedWidth)} | " +
            $"{"Customizable".PadRight(custWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string type = Truncate(r.Type, typeWidth);
            string display = Truncate(r.DisplayName ?? "", displayWidth);
            string name = Truncate(r.Name ?? "", nameWidth);
            OutputWriter.WriteLine(
                $"{type.PadRight(typeWidth)} | " +
                $"{display.PadRight(displayWidth)} | " +
                $"{name.PadRight(nameWidth)} | " +
                $"{r.ObjectId.ToString().PadRight(idWidth)} | " +
                $"{(r.Managed ? "true" : "false").PadRight(managedWidth)} | " +
                $"{(r.Customizable ? "true" : "false").PadRight(custWidth)}");
        }
    }
#pragma warning restore TXC003

    private static string Truncate(string value, int maxWidth) =>
        value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
```

**Output columns:** `Type`, `DisplayName`, `Name`, `ObjectId`, `Managed`, `Customizable`

#### Step 5 — `sln uninstall-check` (solution-level dependency check)

This is a solution-scoped command (lives under `sln`, not `comp`).

**Service contract — `ISolutionDependencyService` (PR 1 defines only `CheckUninstallAsync`):**

> **SOLID note (I3):** In PR 1, the interface defines only `CheckUninstallAsync`. The other 3 methods are added when PR 3 (component dependencies) is implemented. No `NotImplementedException` stubs.

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionDependencyService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// A single dependency row returned by dependency check APIs.
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
/// Dependency analysis operations (component-level and solution-level).
/// PR 1 defines only CheckUninstallAsync; PR 3 extends the interface.
/// </summary>
public interface ISolutionDependencyService
{
    /// <summary>Check what blocks uninstalling a solution.</summary>
    Task<IReadOnlyList<DependencyRow>> CheckUninstallAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct);
}
```

**Service implementation (just the `CheckUninstallAsync` method for PR 1):**

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseSolutionDependencyService.cs`

```csharp
using System.Net.Http;
using System.Text.Json;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionDependencyService : ISolutionDependencyService
{
    public async Task<IReadOnlyList<DependencyRow>> CheckUninstallAsync(
        string? profileName,
        string solutionUniqueName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Web API function call — note the single quotes around the string parameter.
        var path = $"RetrieveDependenciesForUninstall(SolutionUniqueName='{solutionUniqueName}')";
        var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
        return await ParseDependencyResponse(response, ct).ConfigureAwait(false);
    }

    // PR 3 extends ISolutionDependencyService with GetDependentsAsync, GetRequiredAsync,
    // and CheckDeleteAsync. No stubs here — methods are added to the interface when needed (I3).

    /// <summary>
    /// Parses the standard dependency response format shared by all dependency Web API functions.
    /// </summary>
    internal static async Task<IReadOnlyList<DependencyRow>> ParseDependencyResponse(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        var rows = new List<DependencyRow>();
        if (doc.RootElement.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                rows.Add(new DependencyRow(
                    DependentComponentId: Guid.Parse(item.GetProperty("dependentcomponentobjectid").GetString()!),
                    DependentComponentType: item.GetProperty("dependentcomponenttype").GetInt32(),
                    DependentComponentName: null, // Name resolution done separately (ComponentNameResolver)
                    RequiredComponentId: Guid.Parse(item.GetProperty("requiredcomponentobjectid").GetString()!),
                    RequiredComponentType: item.GetProperty("requiredcomponenttype").GetInt32(),
                    RequiredComponentName: null,
                    DependencyType: item.GetProperty("dependencytype").GetInt32()));
            }
        }

        return rows;
    }
}
```

**Important:** The `RetrieveDependenciesForUninstall` response does NOT include `dependencyid` or `requiredcomponentintroducedversion` (unlike other dependency APIs). The parser handles this by only reading fields common to all dependency responses.

**CLI command:**

**File:** `src/TALXIS.CLI.Features.Environment/Solution/SolutionUninstallCheckCliCommand.cs`

```csharp
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Solution;

[CliReadOnly]
[CliCommand(
    Name = "uninstall-check",
    Description = "Check whether a solution can be safely uninstalled."
)]
public class SolutionUninstallCheckCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(SolutionUninstallCheckCliCommand));

    [CliArgument(Name = "name", Description = "Solution unique name.", Required = true)]
    public required string Name { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("'name' argument is required.");
            return ExitValidationError;
        }

        var service = TxcServices.Get<ISolutionDependencyService>();
        var deps = await service.CheckUninstallAsync(Profile, Name, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(deps, PrintDependencyTable);
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintDependencyTable(IReadOnlyList<DependencyRow> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("Safe to uninstall — no blocking dependencies found.");
            return;
        }

        OutputWriter.WriteLine($"Found {rows.Count} blocking dependencies:");
        OutputWriter.WriteLine();

        // Dependency type: 1=Published, 2=SolutionInternal, 4=Unpublished
        string header = $"{"DepType",8} | {"DependentType",14} | {"DependentId",-36} | {"RequiredType",13} | {"RequiredId",-36}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            string depType = r.DependencyType switch
            {
                1 => "Publish",
                2 => "Internal",
                4 => "Unpub",
                _ => r.DependencyType.ToString()
            };
            OutputWriter.WriteLine(
                $"{depType,8} | {r.DependentComponentType,14} | {r.DependentComponentId,-36} | {r.RequiredComponentType,13} | {r.RequiredComponentId,-36}");
        }
    }
#pragma warning restore TXC003
}
```

#### Step 6 — DI registration for PR 1

Add to `DataverseApplicationServiceCollectionExtensions.AddTxcDataverseApplication()`:

```csharp
services.AddSingleton<ISolutionComponentQueryService, DataverseSolutionComponentQueryService>();
services.AddSingleton<ISolutionDependencyService, DataverseSolutionDependencyService>();
```

---

## PR 2 — Component Layers

### New Group Commands

#### 2a. `ComponentCliCommand` (group: `txc env component`)

**File:** `src/TALXIS.CLI.Features.Environment/Component/ComponentCliCommand.cs`

```csharp
using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component;

[CliCommand(
    Name = "component",
    Alias = "comp",
    Description = "Inspect components independent of a specific solution.",
    Children = new[]
    {
        typeof(Layer.ComponentLayerCliCommand),
    }
)]
public class ComponentCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
```

#### 2b. `ComponentLayerCliCommand` (group: `txc env comp layer`)

**File:** `src/TALXIS.CLI.Features.Environment/Component/Layer/ComponentLayerCliCommand.cs`

```csharp
using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component.Layer;

[CliCommand(
    Name = "layer",
    Description = "Inspect solution layers for a component.",
    Children = new[]
    {
        typeof(ComponentLayerListCliCommand),
        typeof(ComponentLayerShowCliCommand),
    }
)]
public class ComponentLayerCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
```

#### 2c. Update `EnvironmentCliCommand.Children`

**File:** `src/TALXIS.CLI.Features.Environment/EnvironmentCliCommand.cs`

Add `typeof(Component.ComponentCliCommand)` to the `Children` array.

---

### Command: `txc env comp layer list <component-id>`

#### Step 1 — Service contract

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionLayerQueryService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record ComponentLayerRow(
    int Order,
    string SolutionName,
    string? PublisherName,
    string? Name,
    DateTime OverwriteTime,
    string? ComponentJson,
    string? Changes);

public interface ISolutionLayerQueryService
{
    /// <summary>
    /// Returns the full solution layer stack for a component.
    /// </summary>
    Task<IReadOnlyList<ComponentLayerRow>> ListLayersAsync(
        string? profileName,
        string componentId,
        string componentTypeName,
        CancellationToken ct);

    /// <summary>
    /// Returns the active layer's component definition JSON.
    /// </summary>
    Task<string?> GetActiveLayerJsonAsync(
        string? profileName,
        string componentId,
        string componentTypeName,
        CancellationToken ct);
}
```

#### Step 2 — Service implementation

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseSolutionLayerQueryService.cs`

```csharp
using System.Net.Http;
using System.Text.Json;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataverseSolutionLayerQueryService : ISolutionLayerQueryService
{
    public async Task<IReadOnlyList<ComponentLayerRow>> ListLayersAsync(
        string? profileName,
        string componentId,
        string componentTypeName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // $select does NOT reduce payload — msdyn_componentjson and msdyn_changes are always returned.
        var path = $"msdyn_componentlayers?$filter=(msdyn_componentid eq '{componentId}' and msdyn_solutioncomponentname eq '{componentTypeName}')";
        var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        var rows = new List<ComponentLayerRow>();
        if (doc.RootElement.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                rows.Add(new ComponentLayerRow(
                    Order: item.GetProperty("msdyn_order").GetInt32(),
                    SolutionName: item.GetProperty("msdyn_solutionname").GetString() ?? "(unknown)",
                    PublisherName: item.TryGetProperty("msdyn_publishername", out var pub) ? pub.GetString() : null,
                    Name: item.TryGetProperty("msdyn_name", out var n) ? n.GetString() : null,
                    OverwriteTime: item.GetProperty("msdyn_overwritetime").GetDateTime(),
                    ComponentJson: item.TryGetProperty("msdyn_componentjson", out var cj) ? cj.GetString() : null,
                    Changes: item.TryGetProperty("msdyn_changes", out var ch) ? ch.GetString() : null));
            }
        }

        return rows.OrderBy(r => r.Order).ToList();
    }

    public async Task<string?> GetActiveLayerJsonAsync(
        string? profileName,
        string componentId,
        string componentTypeName,
        CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Filter to the Active layer specifically.
        var path = $"msdyn_componentlayers?$filter=(msdyn_componentid eq '{componentId}' and msdyn_solutioncomponentname eq '{componentTypeName}' and msdyn_solutionname eq 'Active')";
        var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("value", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("msdyn_componentjson", out var cj))
                    return cj.GetString();
            }
        }

        return null;
    }
}
```

#### Step 3 — CLI commands

**File:** `src/TALXIS.CLI.Features.Environment/Component/Layer/ComponentLayerListCliCommand.cs`

```csharp
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Layer;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "Show solution layer stack for a component."
)]
public class ComponentLayerListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentLayerListCliCommand));

    [CliArgument(Name = "component-id", Description = "Component GUID.", Required = true)]
    public required string ComponentId { get; set; }

    [CliOption(Name = "--type", Description = "Component type name (e.g. Entity, Attribute, Workflow).", Required = true)]
    public required string Type { get; set; }

    [CliOption(Name = "--show-json", Description = "Show full component JSON per layer.", Required = false)]
    public bool ShowJson { get; set; }

    [CliOption(Name = "--show-changes", Description = "Show delta/diff JSON per layer.", Required = false)]
    public bool ShowChanges { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionLayerQueryService>();
        var layers = await service.ListLayersAsync(Profile, ComponentId, Type, CancellationToken.None).ConfigureAwait(false);

        // Capture flags in locals for the closure.
        bool showJson = ShowJson;
        bool showChanges = ShowChanges;
        OutputFormatter.WriteList(layers, rows => PrintLayersTable(rows, showJson, showChanges));
        return ExitSuccess;
    }

#pragma warning disable TXC003
    private static void PrintLayersTable(IReadOnlyList<ComponentLayerRow> rows, bool showJson, bool showChanges)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No layers found.");
            return;
        }

        int slnWidth = Math.Clamp(rows.Max(r => r.SolutionName.Length), 12, 40);
        int pubWidth = Math.Clamp(rows.Max(r => (r.PublisherName ?? "").Length), 9, 30);
        int nameWidth = Math.Clamp(rows.Max(r => (r.Name ?? "").Length), 4, 30);

        string header =
            $"{"Order",5} | {"Solution".PadRight(slnWidth)} | {"Publisher".PadRight(pubWidth)} | {"Name".PadRight(nameWidth)} | OverwriteTime";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));
        foreach (var r in rows)
        {
            OutputWriter.WriteLine(
                $"{r.Order,5} | " +
                $"{r.SolutionName.PadRight(slnWidth)} | " +
                $"{(r.PublisherName ?? "").PadRight(pubWidth)} | " +
                $"{(r.Name ?? "").PadRight(nameWidth)} | " +
                $"{r.OverwriteTime:u}");

            if (showJson && !string.IsNullOrWhiteSpace(r.ComponentJson))
            {
                OutputWriter.WriteLine("  --- Component JSON ---");
                OutputWriter.WriteLine(PrettyPrintJson(r.ComponentJson));
            }
            if (showChanges && !string.IsNullOrWhiteSpace(r.Changes))
            {
                OutputWriter.WriteLine("  --- Changes ---");
                OutputWriter.WriteLine(PrettyPrintJson(r.Changes));
            }
        }
    }
#pragma warning restore TXC003

    private static string PrettyPrintJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json; // Return raw if not valid JSON
        }
    }
}
```

**File:** `src/TALXIS.CLI.Features.Environment/Component/Layer/ComponentLayerShowCliCommand.cs`

```csharp
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Component.Layer;

[CliReadOnly]
[CliCommand(
    Name = "show",
    Description = "Show the active layer component definition as JSON."
)]
public class ComponentLayerShowCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentLayerShowCliCommand));

    [CliArgument(Name = "component-id", Description = "Component GUID.", Required = true)]
    public required string ComponentId { get; set; }

    [CliOption(Name = "--type", Description = "Component type name (e.g. Entity, Attribute, Workflow).", Required = true)]
    public required string Type { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionLayerQueryService>();
        var json = await service.GetActiveLayerJsonAsync(Profile, ComponentId, Type, CancellationToken.None).ConfigureAwait(false);

        if (json is null)
        {
            Logger.LogWarning("No active layer found for component {ComponentId} of type {Type}.", ComponentId, Type);
            return ExitError;
        }

        // Output raw JSON. Use WriteRaw for pre-serialized JSON passthrough.
        // WriteRaw takes Action (parameterless) — capture json from enclosing scope.
        OutputFormatter.WriteRaw(json, () =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine(PrettyPrint(json));
#pragma warning restore TXC003
        });

        return ExitSuccess;
    }

    private static string PrettyPrint(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}
```

#### Step 4 — DI registration for PR 2

```csharp
services.AddSingleton<ISolutionLayerQueryService, DataverseSolutionLayerQueryService>();
```

---

## PR 3 — Component Dependencies

### New Group Commands

#### 3a. `ComponentDependencyCliCommand` (group: `txc env comp dep`)

**File:** `src/TALXIS.CLI.Features.Environment/Component/Dependency/ComponentDependencyCliCommand.cs`

```csharp
using DotMake.CommandLine;

namespace TALXIS.CLI.Features.Environment.Component.Dependency;

[CliCommand(
    Name = "dependency",
    Alias = "dep",
    Description = "Inspect component dependencies.",
    Children = new[]
    {
        typeof(ComponentDependencyListCliCommand),
        typeof(ComponentDependencyRequiredCliCommand),
        typeof(ComponentDependencyDeleteCheckCliCommand),
    }
)]
public class ComponentDependencyCliCommand
{
    public void Run(CliContext context)
    {
        context.ShowHelp();
    }
}
```

#### 3b. Update `ComponentCliCommand.Children`

Add `typeof(Dependency.ComponentDependencyCliCommand)` to the `Children` array of `ComponentCliCommand`.

---

### Extend `ISolutionDependencyService` and `DataverseSolutionDependencyService`

> **SOLID note (I3):** PR 3 extends the `ISolutionDependencyService` interface with the 3 component-level methods. These were intentionally excluded from PR 1 to avoid `NotImplementedException` stubs.

Add the three methods to `ISolutionDependencyService`:

```csharp
    /// <summary>What depends on this component?</summary>
    Task<IReadOnlyList<DependencyRow>> GetDependentsAsync(
        string? profileName, Guid componentId, int componentType, CancellationToken ct);

    /// <summary>What does this component require?</summary>
    Task<IReadOnlyList<DependencyRow>> GetRequiredAsync(
        string? profileName, Guid componentId, int componentType, CancellationToken ct);

    /// <summary>Can this component be safely deleted?</summary>
    Task<IReadOnlyList<DependencyRow>> CheckDeleteAsync(
        string? profileName, Guid componentId, int componentType, CancellationToken ct);
```

Add their implementations to `DataverseSolutionDependencyService`:

```csharp
public async Task<IReadOnlyList<DependencyRow>> GetDependentsAsync(
    string? profileName, Guid componentId, int componentType, CancellationToken ct)
{
    using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
    var path = $"RetrieveDependentComponents(ObjectId={componentId},ComponentType={componentType})";
    var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
    return await ParseDependencyResponse(response, ct).ConfigureAwait(false);
}

public async Task<IReadOnlyList<DependencyRow>> GetRequiredAsync(
    string? profileName, Guid componentId, int componentType, CancellationToken ct)
{
    using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
    var path = $"RetrieveRequiredComponents(ObjectId={componentId},ComponentType={componentType})";
    var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
    return await ParseDependencyResponse(response, ct).ConfigureAwait(false);
}

public async Task<IReadOnlyList<DependencyRow>> CheckDeleteAsync(
    string? profileName, Guid componentId, int componentType, CancellationToken ct)
{
    using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
    var path = $"RetrieveDependenciesForDelete(ObjectId={componentId},ComponentType={componentType})";
    var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, path, string.Empty);
    return await ParseDependencyResponse(response, ct).ConfigureAwait(false);
}
```

**Important API detail:** The Web API function parameters use **unquoted GUIDs** and **unquoted integers** — no single quotes. Example: `RetrieveDependentComponents(ObjectId=70816501-edb9-4740-a16c-6a5efbc05d84,ComponentType=1)`.

---

### CLI Commands

All three dependency commands share the same pattern. They differ only in which service method they call and what message to show when the list is empty.

**Shared base pattern** (implement as three separate files — do NOT use an abstract base, keep it simple):

Each command has:
- `[CliArgument(Name = "component-id")]` — string, the component GUID
- `[CliOption(Name = "--type", Required = true)]` — int, the component type code

**File:** `src/TALXIS.CLI.Features.Environment/Component/Dependency/ComponentDependencyListCliCommand.cs`

```csharp
[CliReadOnly]
[CliCommand(Name = "list", Description = "Show what depends on this component.")]
public class ComponentDependencyListCliCommand : ProfiledCliCommand
{
    // ... standard logger, ComponentId argument, Type option ...

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<ISolutionDependencyService>();
        var deps = await service.GetDependentsAsync(Profile, Guid.Parse(ComponentId), Type, CancellationToken.None).ConfigureAwait(false);
        OutputFormatter.WriteList(deps, PrintDependencyTable);
        return ExitSuccess;
    }
    // ... shared table printer (see uninstall-check pattern above) ...
}
```

**File:** `src/TALXIS.CLI.Features.Environment/Component/Dependency/ComponentDependencyRequiredCliCommand.cs`

Same pattern, calls `service.GetRequiredAsync(...)`. Empty message: `"No required dependencies — this component has no upstream requirements."`

**File:** `src/TALXIS.CLI.Features.Environment/Component/Dependency/ComponentDependencyDeleteCheckCliCommand.cs`

Same pattern, calls `service.CheckDeleteAsync(...)`. Empty message: `"No blocking dependencies — safe to delete."`

**Output columns for all three:** `DepType`, `DependentType`, `DependentId`, `RequiredType`, `RequiredId`

(Name resolution via `ComponentNameResolver` from Phase 0 will enrich these later — for the initial implementation, show GUIDs and type codes.)

---

## PR 4 — Component Export

### `txc env comp export <component-id>`

This is the most complex command. It creates a temporary solution, exports it, unpacks it, and cleans up.

**File:** `src/TALXIS.CLI.Features.Environment/Component/ComponentExportCliCommand.cs`

#### Service contract

Add to a new file or extend an existing one:

**File:** `src/TALXIS.CLI.Core/Contracts/Dataverse/IComponentExportService.cs`

```csharp
namespace TALXIS.CLI.Core.Contracts.Dataverse;

public sealed record ComponentExportResult(
    string OutputPath,
    int FileCount);

public interface IComponentExportService
{
    Task<ComponentExportResult> ExportAsync(
        string? profileName,
        Guid componentId,
        int componentType,
        string outputDirectory,
        bool includeRequired,
        CancellationToken ct);
}
```

#### Service implementation sketch

**File:** `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseComponentExportService.cs`

```csharp
internal sealed class DataverseComponentExportService : IComponentExportService
{
    public async Task<ComponentExportResult> ExportAsync(
        string? profileName, Guid componentId, int componentType,
        string outputDirectory, bool includeRequired, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        var tempSolutionName = $"_txc_export_{DateTime.UtcNow:yyyyMMddHHmmss}";

        try
        {
            // 1. Create temporary unmanaged solution
            //    SDK: CreateRequest on "solution" entity
            var createResponse = await conn.Client.CreateAsync(new Entity("solution")
            {
                ["uniquename"] = tempSolutionName,
                ["friendlyname"] = $"txc temp export ({componentId})",
                ["version"] = "1.0.0.0",
                ["publisherid"] = new EntityReference("publisher", defaultPublisherId),
            }, ct).ConfigureAwait(false);

            // 2. Add the target component
            //    SDK: AddSolutionComponentRequest
            var addRequest = new Microsoft.Crm.Sdk.Messages.AddSolutionComponentRequest
            {
                ComponentId = componentId,
                ComponentType = componentType,
                SolutionUniqueName = tempSolutionName,
                AddRequiredComponents = includeRequired,
            };
            await conn.Client.ExecuteAsync(addRequest, ct).ConfigureAwait(false);

            // 3. Export as unmanaged ZIP
            //    SDK: ExportSolutionRequest
            var exportRequest = new Microsoft.Crm.Sdk.Messages.ExportSolutionRequest
            {
                SolutionName = tempSolutionName,
                Managed = false,
            };
            var exportResponse = (Microsoft.Crm.Sdk.Messages.ExportSolutionResponse)
                await conn.Client.ExecuteAsync(exportRequest, ct).ConfigureAwait(false);
            byte[] zipBytes = exportResponse.ExportSolutionFile;

            // 4. Unpack via SolutionPackagerLib (see Phase 0 infrastructure)
            //    Write ZIP to a temp file, call SolutionPackager.Run(), delete temp file.
            //    ISolutionPackagerService.UnpackAsync(zipBytes, outputDirectory)

            // 5. Return result
            var fileCount = Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories).Length;
            return new ComponentExportResult(outputDirectory, fileCount);
        }
        finally
        {
            // 6. ALWAYS delete the temporary solution (cleanup)
            try
            {
                // Resolve the temp solution ID and delete it.
                // SDK: DeleteRequest on the solution entity
            }
            catch
            {
                // Log but don't throw — the export result is still valid.
            }
        }
    }
}
```

**SDK NuGet reference required:** `Microsoft.CrmSdk.CoreAssemblies` and `Microsoft.CrmSdk.Messages` for `AddSolutionComponentRequest`, `ExportSolutionRequest`. These are likely already referenced by the project — verify in `TALXIS.CLI.Platform.Dataverse.Application.csproj`.

#### CLI command

**File:** `src/TALXIS.CLI.Features.Environment/Component/ComponentExportCliCommand.cs`

```csharp
[CliIdempotent] // Temp solution is created and deleted — no persistent state, but mutative during execution. CliIdempotent because re-running is safe.
[CliCommand(Name = "export", Description = "Export a single component's definition as unpacked solution files.")]
public class ComponentExportCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ComponentExportCliCommand));

    [CliArgument(Name = "component-id", Description = "Component GUID.", Required = true)]
    public required string ComponentId { get; set; }

    [CliOption(Name = "--type", Description = "Component type code (integer).", Required = true)]
    public required int Type { get; set; }

    [CliOption(Name = "--output", Aliases = new[] { "-o" }, Description = "Output directory (default: current directory).", Required = false)]
    public string? Output { get; set; }

    [CliOption(Name = "--include-required", Description = "Also include required dependencies.", Required = false)]
    public bool IncludeRequired { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var outputDir = Output ?? Environment.CurrentDirectory;
        var service = TxcServices.Get<IComponentExportService>();
        var result = await service.ExportAsync(
            Profile, Guid.Parse(ComponentId), Type, outputDir, IncludeRequired, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(result);
        Logger.LogInformation("Exported {FileCount} files to {OutputPath}", result.FileCount, result.OutputPath);
        return ExitSuccess;
    }
}
```

#### Update `ComponentCliCommand.Children`

Add `typeof(ComponentExportCliCommand)` to the `Children` array.

#### DI registration

```csharp
services.AddSingleton<IComponentExportService, DataverseComponentExportService>();
```

---

## Summary Checklist

### Files to create (by PR)

**PR 1:**
| # | File | Type |
|---|------|------|
| 1 | `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionDetailService.cs` | Interface + DTOs |
| 2 | `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionComponentQueryService.cs` | Interface + DTOs |
| 3 | `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionDependencyService.cs` | Interface + DTOs (PR 1: `CheckUninstallAsync` only) |
| 4 | `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseSolutionDetailService.cs` | Implementation |
| 5 | `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseSolutionComponentQueryService.cs` | Implementation |
| 6 | `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseSolutionDependencyService.cs` | Implementation (`CheckUninstallAsync` only — no stubs) |
| 7 | `src/TALXIS.CLI.Features.Environment/Solution/SolutionShowCliCommand.cs` | CLI command |
| 8 | `src/TALXIS.CLI.Features.Environment/Solution/SolutionUninstallCheckCliCommand.cs` | CLI command |
| 9 | `src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentCliCommand.cs` | Group command |
| 10 | `src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentCountCliCommand.cs` | CLI command |
| 11 | `src/TALXIS.CLI.Features.Environment/Solution/Component/SolutionComponentListCliCommand.cs` | CLI command |

**Files to modify (PR 1):**
| File | Change |
|------|--------|
| `SolutionCliCommand.cs` | Add 3 entries to `Children` |
| `DataverseApplicationServiceCollectionExtensions.cs` | Add 3 `AddSingleton` lines |

**PR 2:**
| # | File | Type |
|---|------|------|
| 1 | `src/TALXIS.CLI.Core/Contracts/Dataverse/ISolutionLayerQueryService.cs` | Interface + DTOs |
| 2 | `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseSolutionLayerQueryService.cs` | Implementation |
| 3 | `src/TALXIS.CLI.Features.Environment/Component/ComponentCliCommand.cs` | Group command |
| 4 | `src/TALXIS.CLI.Features.Environment/Component/Layer/ComponentLayerCliCommand.cs` | Group command |
| 5 | `src/TALXIS.CLI.Features.Environment/Component/Layer/ComponentLayerListCliCommand.cs` | CLI command |
| 6 | `src/TALXIS.CLI.Features.Environment/Component/Layer/ComponentLayerShowCliCommand.cs` | CLI command |

**Files to modify (PR 2):**
| File | Change |
|------|--------|
| `EnvironmentCliCommand.cs` | Add `ComponentCliCommand` to `Children` |
| `DataverseApplicationServiceCollectionExtensions.cs` | Add 1 `AddSingleton` line |

**PR 3:**
| # | File | Type |
|---|------|------|
| 1 | `src/TALXIS.CLI.Features.Environment/Component/Dependency/ComponentDependencyCliCommand.cs` | Group command |
| 2 | `src/TALXIS.CLI.Features.Environment/Component/Dependency/ComponentDependencyListCliCommand.cs` | CLI command |
| 3 | `src/TALXIS.CLI.Features.Environment/Component/Dependency/ComponentDependencyRequiredCliCommand.cs` | CLI command |
| 4 | `src/TALXIS.CLI.Features.Environment/Component/Dependency/ComponentDependencyDeleteCheckCliCommand.cs` | CLI command |

**Files to modify (PR 3):**
| File | Change |
|------|--------|
| `ComponentCliCommand.cs` | Add `Dependency.ComponentDependencyCliCommand` to `Children` |
| `ISolutionDependencyService.cs` | Extend interface with `GetDependentsAsync`, `GetRequiredAsync`, `CheckDeleteAsync` |
| `DataverseSolutionDependencyService.cs` | Add the three new method implementations |

**PR 4:**
| # | File | Type |
|---|------|------|
| 1 | `src/TALXIS.CLI.Core/Contracts/Dataverse/IComponentExportService.cs` | Interface + DTOs |
| 2 | `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseComponentExportService.cs` | Implementation |
| 3 | `src/TALXIS.CLI.Features.Environment/Component/ComponentExportCliCommand.cs` | CLI command |

**Files to modify (PR 4):**
| File | Change |
|------|--------|
| `ComponentCliCommand.cs` | Add `ComponentExportCliCommand` to `Children` |
| `DataverseApplicationServiceCollectionExtensions.cs` | Add 1 `AddSingleton` line |

---

## API Quick Reference

| Command | API | Method | Key quirk |
|---------|-----|--------|-----------|
| `sln show` | `solutions` + `msdyn_solutioncomponentcountsummaries` | SDK `QueryExpression` + Web API | Filter by `uniquename` (SDK), by `msdyn_solutionid` GUID (Web API) |
| `sln component count` | `msdyn_solutioncomponentcountsummaries` | Web API | `msdyn_solutionid` is null in response rows — only useful as filter |
| `sln component list` | `msdyn_solutioncomponentsummaries` | Web API | Boolean filters cause BadRequest — filter client-side |
| `sln uninstall-check` | `RetrieveDependenciesForUninstall(SolutionUniqueName='...')` | Web API function | Takes `SolutionUniqueName` (string with quotes), not GUID |
| `comp layer list` | `msdyn_componentlayers` | Web API | `$select` doesn't reduce payload; `msdyn_componentjson` always returned |
| `comp layer show` | `msdyn_componentlayers` + `msdyn_solutionname eq 'Active'` filter | Web API | Same as above, filtered to Active layer |
| `comp dep list` | `RetrieveDependentComponents(ObjectId=...,ComponentType=...)` | Web API function | No quotes around GUID/int params |
| `comp dep required` | `RetrieveRequiredComponents(ObjectId=...,ComponentType=...)` | Web API function | Same pattern |
| `comp dep delete-check` | `RetrieveDependenciesForDelete(ObjectId=...,ComponentType=...)` | Web API function | Same pattern |
| `comp export` | `CreateRequest` + `AddSolutionComponentRequest` + `ExportSolutionRequest` + `DeleteRequest` | SDK | Always cleanup temp solution in `finally` |

> **Do NOT use `*WithMetadata` variants** of dependency APIs — they return empty arrays `[]` in all tested scenarios. Use non-metadata variants and resolve names separately via `ComponentNameResolver`.
