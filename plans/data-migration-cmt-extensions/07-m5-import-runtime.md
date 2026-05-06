# Milestone 5 — Import Runtime + Idempotency and Alternate-Key Resolution

> Goal: After CMT import, apply the sidecar-defined non-CMT operations (owners, state, BPF, custom APIs, workflows) idempotently and in a controlled order. Make repeated imports converge to the same state and let sidecars (and, where possible, CMT itself) reference records by natural keys instead of GUIDs.

## Architecture

A new in-process post-processor runs as Phase B of `txc data package import`:

```
import command
 ├─ Phase A: existing CMT engine (subprocess)
 │     - creates/updates records
 │     - applies CMT-known ownerid, statecode/statuscode (will be overridden by Phase B if sidecars say so)
 │
 └─ Phase B: PostImportRunner (new, in-process)
       1. Load manifest + sidecars
       2. Resolve all references (ids, alt keys -> guids) using KeyResolver
       3. Execute steps in postimport order, each step batched
       4. Emit a structured run report
```

Phase B is skipped automatically when no `txc-package.xml` is present (back-compat).

## Request Headers (from manifest `<options>`)

The PostImportRunner resolves request headers from CLI flags, manifest defaults, entity options, and step overrides. Headers are **not blindly applied to every Phase B operation**: post-import business logic often needs plugins/flows enabled, so each step defaults to normal Dataverse behavior unless the package or step explicitly opts into bypass.

| Manifest option | HTTP header | Purpose |
|---|---|---|
| `<plugins bypass="true">` | `BypassCustomPluginExecution: true` | Skip all custom plugins |
| `<bypassBusinessLogic>CustomAsync</bypassBusinessLogic>` | `MSCRM.BypassBusinessLogicExecution: CustomAsync` | Skip only async plugins (or `CustomSync` for sync-only) |
| `<bypassPluginStepIds><step id="..."/></bypassPluginStepIds>` | `MSCRM.BypassBusinessLogicExecutionStepIds: guid1,guid2` | Bypass specific plugin steps |
| `<suppressPowerAutomateFlows>true</suppressPowerAutomateFlows>` | `MSCRM.SuppressCallbackRegistrationExpanderJob: true` | **Bypass Power Automate flows** — critical for migration perf |
| `<suppressDuplicateDetection>true</suppressDuplicateDetection>` | `MSCRM.SuppressDuplicateDetection: true` | Skip duplicate detection rules |
| `<autoDisassociate>true</autoDisassociate>` | `AutoDisassociate: true` | Auto-remove old N:1 associations when updating lookups |
| `<coalesceNonEmptyValues>true</coalesceNonEmptyValues>` | *(logic in PostImportRunner)* | Only include non-empty fields in update requests; empty = don't touch |

Per-record `MSCRMCallerID` is applied from `data_callerid.xml` sidecar.

Step-level overrides can narrow or disable package defaults:

```xml
<postimport>
  <step kind="owners" suppressPowerAutomateFlows="true" />
  <step kind="actions" bypassBusinessLogic="none" suppressPowerAutomateFlows="false" />
</postimport>
```

Guideline: use bypass aggressively for pure load/correction steps only when the migration owns the resulting side effects. Keep bypass off for Custom API, workflow, or BPF steps whose purpose is to run target business logic.

## Retry / Throttling

The PostImportRunner retries on transient Dataverse errors:

| Error | Retry strategy |
|---|---|
| HTTP 429 (Too Many Requests) | Wait `Retry-After` header value, fallback 90s |
| HTTP 503 (Service Unavailable) | Wait 90s |
| Dataverse throttling codes (`-2147015903`, `-2147015902`, `-2147015898`) | Wait 90s |
| SQL timeout (`-2147204784` with `-2146232060`) | Wait 30s |

Max retries: 3 per batch. Exponential backoff if no `Retry-After` header. Investigate whether the existing Dataverse service client (`ServiceClient`) already handles this — if so, just verify and document; if not, add retry wrapper.

## New Dataverse Services (DI-registered)

| Interface | Purpose | Underlying request(s) |
|---|---|---|
| `IDataverseAssignmentService` | Change record owner. | `AssignRequest` |
| `IDataverseStateService` | Set statecode/statuscode. | `SetStateRequest`; falls back to `Update` for entities Microsoft moved off `SetStateRequest`. |
| `IDataverseProcessService` | Move BPF stage. | `RetrieveProcessInstancesRequest`, `SetProcessRequest`, `Create` (BPF entity), `Update` (`activestageid`). |
| `IDataverseExecutionService` | Run Custom API / Custom Action / arbitrary `OrganizationRequest`. | Generic `Execute` on `IOrganizationService`. |
| `IDataverseWorkflowService` | Trigger classic workflows / on-demand flows. | `ExecuteWorkflowRequest`. |

Each service exposes both single and batch (`ExecuteMultipleRequest`) variants. They live in `TALXIS.CLI.Platform.Dataverse.Application.Services` and are registered via `DataverseApplicationServiceCollectionExtensions`.

These services are *generally useful* — they will also be exposed as standalone `txc data record` commands (see CLI surface below).

## PostImportRunner

```
TALXIS.CLI.Features.Data/Package/PostImport/
├── IPostImportRunner.cs
├── PostImportRunner.cs           # orchestrates steps
├── Steps/
│   ├── OwnersStep.cs
│   ├── StateStep.cs
│   ├── BpfStep.cs
│   └── ActionsStep.cs            # handles both <call> and <execute workflow>
├── ReferenceResolver.cs          # uses KeyResolver + cache
└── PostImportReport.cs
```

Behavior:

- Each step batches operations of the same kind into `ExecuteMultipleRequest`s sized via existing `--batch-size`.
- Errors are collected per-record. `continueOnError` (manifest or `--continue-on-error` flag) decides whether to abort the step.
- All operations are logged via `ILogger` (diagnostics) and summarized via `OutputWriter` (results).
- Dry-run mode (`--dry-run`) plans operations and prints the report without calling Dataverse.

## Idempotency Rules per Step

| Step | How it stays idempotent |
|---|---|
| Owners | Compare current `ownerid` before issuing `AssignRequest`; skip if already correct. |
| State | Compare current `statecode`/`statuscode`; skip if already at target. |
| BPF | Compare current `activestageid`; skip if already at target. Also no-op if the BPF instance exists at the right stage. |
| Actions | Two modes: `default` always runs (caller chose to express it); `idempotent` runs only when a probe condition is satisfied. |

`<call>` and `<execute>` accept an optional `<when>` probe:

```xml
<call name="new_promoteRecord" idempotent="true">
  <bind type="EntityReference" name="Target" entity="account" id="…" />
  <when>
    <fetch>
      <entity name="account">
        <attribute name="accountid" />
        <filter>
          <condition attribute="accountid" operator="eq" value="…" />
          <condition attribute="statuscode" operator="ne" value="100000001" />
        </filter>
      </entity>
    </fetch>
  </when>
</call>
```

The probe is a FetchXML that, when returning ≥1 row, signals "still needs running". If empty → skipped.

### `<patch>` Query-Then-Update

The `<patch>` element queries for records and updates them. Idempotency is inherent — if the field already has the target value, the update is a no-op at the Dataverse level.

### Idempotency Strategy Across Phases

| Operation source | Idempotency |
|---|---|
| `data.xml` records (CMT) | Match-by-GUID. With Mechanism A active, the GUID is derived from natural keys, so re-runs match the same row. |
| Sidecar references in `data_owners.xml`, etc. | Resolved via Mechanism B at apply time. Step probes ensure no-op when target state matches. |
| Custom API `<call>` ops | Opt-in idempotent via `<when>` FetchXML probe. |
| Workflow `<execute>` ops | Always re-runs unless guarded by `<when>`. |

## Alternate-Key Resolution

CMT's idempotency is weak by design:

- Hard-coded `enabledDuplicateDetection: false`.
- Dedup only by GUID, with `primarynamefield` as a single fallback.
- No alternate-key support at all.

Real legacy migrations almost never have stable GUIDs in the source system. Records carry natural keys (customer number, ledger code, email) that the migration must preserve as the matching axis across re-runs.

### Two Complementary Mechanisms

#### Mechanism A — Deterministic GUID Synthesis (existing primitive)

The `Transform` server already exposes `/ComputePrimaryKey` (MD5 over `(entity, sortedAlternateKeyTuple)`). Promote this to:

- A first-class library service `IRecordKeyService.ComputeId(entity, IDictionary<string,string> alt)` re-using the same MD5 algorithm so HTTP and library outputs match byte-for-byte.
- A pre-stage of `txc data package convert` that, when `data_keys.xml` declares a key for an entity and the key fields are filled, **rewrites the record `id` to the deterministic GUID** before emitting `data.xml`.

Result: subsequent CMT imports match by GUID (which CMT *does* support), and re-runs converge.

The alternate-key tuple must include the full uniqueness scope. In multi-company or multi-source migrations, fields such as `source_system`, `company_code`, `migration_partition`, or another partition discriminator should be declared as key fields. Do not rely on a local legacy id alone if that id can repeat across partitions.

#### Mechanism B — Runtime Alternate-Key Resolution (new)

For sidecars and for `data.xml` records whose ids cannot be synthesized (e.g. lookups into existing-in-target records), txc resolves via FetchXML at apply time:

```
ReferenceResolver.Resolve("account", key="byNumber", value="ACC-001")
  -> FetchXML: <fetch><entity name="account"><attribute name="accountid" /><filter>
       <condition attribute="accountnumber" operator="eq" value="ACC-001" /></filter></entity></fetch>
  -> caches result; multiple references to the same key share one query
  -> 0 hits  -> error (or auto-create stub if --create-stubs is set, future)
  -> 2+ hits -> error (key was not actually unique)
```

Lookups are read-only and don't require Dataverse alternate keys to be physically defined — txc treats them as "convention".

### `IRecordKeyService`

```
Package/Keys/
├── IRecordKeyService.cs         # Compute(entity, dict) + Resolve(entity, key, value, profile, ct)
├── RecordKeyService.cs          # MD5 implementation (shared with HTTP server)
├── KeyDictionary.cs             # parsed data_keys.xml in-memory model
└── KeyAwareXmlRewriter.cs       # used by convert to rewrite ids and lookup tokens
```

Wire `IRecordKeyService` into DI; replace the inline MD5 in `ComputePrimaryKeyController` with a call to the same service.

### Failure Semantics

- Mechanism A on a record without all alt-key fields filled → hard error during `convert` (clear list of missing fields).
- Mechanism B with 0 hits → hard error during apply, with the offending key/value/entity in the message.
- Mechanism B with >1 hit → hard error; key declaration is wrong, not the data.
- Brownfield collision: if deterministic GUID synthesis would create a new id but the same alternate key already exists in target under a different GUID, Phase 1 fails by default before import. The operator must choose a strategy explicitly: use the existing target GUID in Excel, fix the key tuple, or defer to a future key-map/merge workflow.
- Existing target record with same deterministic GUID but conflicting non-key data is not a key-resolution error. It is handled as an update by CMT and should be surfaced by pre-import validation / post-import validation depending on command flow.

### Edge Cases

- **Self-references / hierarchical data**: Mechanism A's deterministic GUIDs solve cycles for free — both ends of a self-reference resolve to the same predictable GUID.
- **Composite keys**: `data_keys.xml` allows multi-field keys. Hash inputs are sorted alphabetically by field logical name to keep hashing canonical.
- **Lookups across entities**: A lookup column whose value is `keyref:byNumber=ACC-001` is rewritten to the synthesized GUID for the target entity at convert time (when Mechanism A is enabled for the target entity), or left as a `keyref:` token resolved at apply time (Mechanism B).
- **Files / annotations**: Annotations and file columns inherit the parent record id and need no special handling.

### Future Key Map / Load Journal

Phase 1 does not introduce a persistent key-map database or rollback journal. That is intentional: the first implementation keeps the CMT package source-controllable and idempotent. A later milestone should add a load journal for brownfield merge decisions, rollback planning, and mapping source keys to target GUIDs when deterministic IDs are not safe.

### Key Resolution CLI Surface

```
txc data package convert
  ...
  --keys data_keys.xml          # enables Mechanism A
  --keys-from-package <path>    # pulls data_keys.xml from a package folder

txc data record resolve-key
  --entity account
  --key byNumber
  --value ACC-001
  [--profile ...]               # required to query
```

`txc data record resolve-key` is a thin wrapper around `IRecordKeyService.ResolveAsync` for scripting and CI checks.

## Authoring Custom API Parameter Binding

Generic mapping table (already used internally by Dataverse SDK):

| `type` attribute | C# / SDK type |
|---|---|
| `String` | `string` |
| `Int` | `int` |
| `Bool` | `bool` |
| `Decimal` | `decimal` |
| `Money` | `Money` |
| `DateTime` | `DateTime` |
| `Guid` | `Guid` |
| `OptionSetValue` | `OptionSetValue(int)` |
| `EntityReference` | `EntityReference(entity, id|keyRef)` |
| `EntityCollection` | nested `<entityCollection>` with `<entity>` children |
| `Entity` | nested `<entity>` with `<attribute>` children |

Unknown types → hard error with helpful list of supported types.

## Per-Step CLI Flags (added to `import`)

```
--skip-postimport               # run Phase A only
--postimport-only               # skip Phase A, assume CMT already ran
--continue-on-error             # let steps continue past errors (manifest can override)
--dry-run                       # plan + report; do not call Dataverse
--report <path>                 # write the structured run report (JSON)
```

## Run Report

JSON document via `TxcOutputJsonOptions.Default`:

```json
{
  "phase": "postimport",
  "steps": [
    {
      "kind": "owners",
      "total": 12, "applied": 8, "skipped": 4, "failed": 0,
      "duration": "00:00:01.234",
      "items": [
        { "entity": "account", "id": "...", "status": "applied", "from": "...", "to": "..." }
      ]
    }
  ],
  "succeeded": true
}
```

Suitable for CI dashboards and post-mortem.

## New CLI Commands (`txc data record`)

Individual record-level commands for debugging and manual operations. These are the same primitives used by Phase B; exposing them at the CLI keeps "scripted single-record" use cases in the same toolbox and makes Phase B testable end-to-end.

| Command | Purpose |
|---|---|
| `txc data record assign --entity X --id … --owner …` | Wraps `IDataverseAssignmentService`. |
| `txc data record set-state --entity X --id … --state … --status …` | Wraps `IDataverseStateService`. |
| `txc data record set-bpf --entity X --id … --process … --stage …` | Wraps `IDataverseProcessService`. |
| `txc data record execute-action --name … [--bind …] [--param …]` | Wraps `IDataverseExecutionService` for Custom API/Action. |
| `txc data record execute-workflow --workflow … --id …` | Wraps `IDataverseWorkflowService`. |
| `txc data record resolve-key --entity X --key K --value V` | Wraps `IRecordKeyService.ResolveAsync`. |

## Imperative Extensibility (DEFERRED)

A `PackageExtension` base class with `PreImport()`, `PostImport()`, and `PostProcess()` hooks is planned but **deferred** within this milestone. The declarative sidecar approach covers all current requirements. Imperative extensibility will be added when a concrete use case demands it.

## Implementation

### New Types

| Type                         | Location                                                          | Responsibility                           |
|------------------------------|-------------------------------------------------------------------|------------------------------------------|
| `PostImportRunner`           | `TALXIS.CLI.Features.Data/Package/PostImport/`                    | Orchestrates Phase B                     |
| `OwnersStep`                | `TALXIS.CLI.Features.Data/Package/PostImport/Steps/`              | Processes `data_owners.xml`              |
| `StateStep`                 | `TALXIS.CLI.Features.Data/Package/PostImport/Steps/`              | Processes `data_state.xml`               |
| `BpfStep`                   | `TALXIS.CLI.Features.Data/Package/PostImport/Steps/`              | Processes `data_bpf.xml`                 |
| `ActionsStep`               | `TALXIS.CLI.Features.Data/Package/PostImport/Steps/`              | Handles both `<call>` and `<execute workflow>` |
| `ReferenceResolver`         | `TALXIS.CLI.Features.Data/Package/PostImport/`                    | Uses KeyResolver + cache                 |
| `PostImportReport`          | `TALXIS.CLI.Features.Data/Package/PostImport/`                    | Run report model + serialization         |
| `IDataverseAssignmentService`| `TALXIS.CLI.Platform.Dataverse.Application/Services/`            | AssignRequest wrapper                    |
| `IDataverseStateService`    | `TALXIS.CLI.Platform.Dataverse.Application/Services/`             | SetStateRequest wrapper                  |
| `IDataverseProcessService`  | `TALXIS.CLI.Platform.Dataverse.Application/Services/`             | BPF operations                           |
| `IDataverseExecutionService`| `TALXIS.CLI.Platform.Dataverse.Application/Services/`             | Custom API execution                     |
| `IDataverseWorkflowService` | `TALXIS.CLI.Platform.Dataverse.Application/Services/`             | Workflow execution                       |
| `IRecordKeyService`         | `TALXIS.CLI.Features.Data/Package/Keys/`                          | Deterministic GUID + key resolution      |
| `RecordKeyService`          | `TALXIS.CLI.Features.Data/Package/Keys/`                          | MD5 implementation (shared with HTTP server) |
| `KeyDictionary`             | `TALXIS.CLI.Features.Data/Package/Keys/`                          | Parsed data_keys.xml in-memory model     |
| `KeyAwareXmlRewriter`       | `TALXIS.CLI.Features.Data/Package/Keys/`                          | Rewrites ids and lookup tokens at convert time |

## Tests

| Test                              | Description                                                        |
|-----------------------------------|--------------------------------------------------------------------|
| Full import with sidecars         | Mock `IOrganizationService` → assert correct request types/order per sidecar |
| Idempotency                       | Re-running against an already-aligned environment produces zero `applied`, all `skipped` |
| Owner assignment                  | Record with owner sidecar → `AssignRequest` with correct target    |
| State transition                  | Record with state sidecar → `SetStateRequest` with correct codes; falls back to `Update` |
| BPF advancement                   | Record with BPF sidecar → correct stage resolution + advancement   |
| Actions execution                 | `<call>` + `<when>` probe → skip if already applied                |
| Patch query-then-update           | `<patch>` → FetchXML query + Update request                       |
| Workflow execution                | `<execute workflow>` → `ExecuteWorkflowRequest`                    |
| Continue on error                 | One record fails → others still processed                          |
| Dry run                           | All checks run, zero writes issued                                 |
| Run report                        | Assert report JSON matches expected structure                      |
| Deterministic GUID                | Same alt-key tuple from HTTP and library returns the same GUID byte-for-byte |
| KeyAwareXmlRewriter               | Rewrites both record ids and lookup tokens correctly               |
| Resolver caching                  | 100 references to same key issue 1 query                           |
| Re-import convergence             | Import package twice into a clean env; second run shows zero CMT writes |
| Failure modes                     | Missing user, invalid stage name, unknown custom API → clear error, report contains offending item |

## Done When

- An import that includes sidecars produces the expected post-state for owners / state / BPF / Custom API / workflow
- Re-running the same import is a no-op (zero applied operations in the report)
- A package authored with only natural keys (no GUIDs) round-trips through `convert` → `import` → `import again` with zero net change
- `txc data record resolve-key` is documented and exits non-zero on 0 / >1 hits
- All new CLI commands work for manual debugging
- Run report accurately summarizes the import
- Documentation: new `docs/data-migration-runtime.md` covers Phase B end-to-end; `docs/configuration-migration.md` gains an "Idempotency and Keys" section
- All tests pass
