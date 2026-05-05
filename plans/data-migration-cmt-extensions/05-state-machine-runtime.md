# M4 — State-Machine Runtime (Post-Import Operations)

> Goal: After CMT import, apply the sidecar-defined non-CMT operations (owners, state, BPF, custom APIs, workflows) idempotently and in a controlled order.

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
       2. Resolve all references (ids, alt keys -> guids) using M5 KeyResolver
       3. Execute steps in postimport order, each step batched
       4. Emit a structured run report
```

Phase B is skipped automatically when no `txc-package.xml` is present (back-compat).

## New services (DI-registered)

| Interface | Purpose | Underlying request(s) |
|---|---|---|
| `IDataverseAssignmentService` | Change record owner. | `AssignRequest` |
| `IDataverseStateService` | Set statecode/statuscode. | `SetStateRequest`; falls back to `Update` for entities Microsoft moved off `SetStateRequest`. |
| `IDataverseProcessService` | Move BPF stage. | `RetrieveProcessInstancesRequest`, `SetProcessRequest`, `Create` (BPF entity), `Update` (`activestageid`). |
| `IDataverseExecutionService` | Run Custom API / Custom Action / arbitrary `OrganizationRequest`. | Generic `Execute` on `IOrganizationService`. |
| `IDataverseWorkflowService` | Trigger classic workflows / on-demand flows. | `ExecuteWorkflowRequest`. |

Each service exposes both single and batch (`ExecuteMultipleRequest`) variants. They live in `TALXIS.CLI.Platform.Dataverse.Application.Services` and are registered via `DataverseApplicationServiceCollectionExtensions`.

These services are *generally useful* — they will also be exposed as standalone `txc data record` commands in the same milestone (see CLI surface in `07`).

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
├── ReferenceResolver.cs          # uses M5 KeyResolver + cache
└── PostImportReport.cs
```

Behavior:

- Each step batches operations of the same kind into `ExecuteMultipleRequest`s sized via existing `--batch-size`.
- Errors are collected per-record. `continueOnError` (manifest or `--continue-on-error` flag) decides whether to abort the step.
- All operations are logged via `ILogger` (diagnostics) and summarized via `OutputWriter` (results).
- Dry-run mode (`--dry-run`) plans operations and prints the report without calling Dataverse.

## Idempotency rules per step

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

## Authoring custom API parameter binding

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

## Per-step CLI flags (added to `import`)

```
--skip-postimport               # run Phase A only
--postimport-only               # skip Phase A, assume CMT already ran
--continue-on-error             # let steps continue past errors (manifest can override)
--dry-run                       # plan + report; do not call Dataverse
--report <path>                 # write the structured run report (JSON)
```

## Run report

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

## Tests

- Mock `IOrganizationService` and assert correct request types/order per sidecar.
- Idempotency: re-running against an already-aligned environment produces zero `applied`, all `skipped`.
- Failure modes: missing user, invalid stage name, unknown custom API → clear error, report contains the offending item.
- Dry-run does not call any service.

## Done when

- An import that includes sidecars produces the expected post-state for owners / state / BPF / Custom API / workflow.
- Re-running the same import is a no-op (zero applied operations in the report).
- Documentation: new `docs/data-migration-runtime.md` covers Phase B end-to-end.
