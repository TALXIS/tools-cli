# Assessment — Current State of `txc data` and CMT

This file captures the research that informs the rest of the plan. Cite when reviewing milestone designs.

## txc CLI — what already exists

### `data` command tree

| Command | Class | Notes |
|---|---|---|
| `data package import` | `DataPackageImportCliCommand` | Wraps CMT `ImportCrmDataHandler` via `CmtImportRunner` (subprocess). Supports `--connection-count`, `--batch-mode`, `--batch-size`, `--override-safety-checks`, `--prefetch-limit`. |
| `data package export` | `DataPackageExportCliCommand` | Wraps CMT `ExportCrmDataHandler` via `CmtExportRunner`. Supports `--export-files`, `--zip`, `--overwrite`. Requires a `--schema` file. |
| `data package convert` | `DataPackageConvertCliCommand` | Local XLSX → CMT data XML. Naïve: no type coercion, no schema awareness, table per worksheet table. |
| `data model convert` | `DataModelConvertCliCommand` | Solution model → DBML / SQL / EDMX / Ribbon. Not directly relevant but useful inspiration for "schema in, artifact out" pattern. |
| `data transform server` | `TransformCliCommand` + `DataTransformationServer` | HTTP server with `/ComputePrimaryKey` endpoint (deterministic GUID from alternate keys via MD5). Useful for M5. |

### Reusable services (DI-registered today)

| Service | Capabilities |
|---|---|
| `IDataPackageService` | Import/Export CMT packages (calls into `CmtImportRunner` / `CmtExportRunner`). |
| `IDataverseQueryService` | Read-only SQL/FetchXML/OData. |
| `IDataverseRecordService` | Single-record `GetAsync` / `CreateAsync` / `UpdateAsync` / `DeleteAsync`. |
| `IDataverseBulkService` | `CreateMultipleAsync` / `UpdateMultipleAsync` / `UpsertMultipleAsync`. |
| `IDataverseEntityMetadataService` | List entities, describe attributes, list/CRUD relationships. Uses `RetrieveAllEntitiesRequest` + `RetrieveEntityRequest(EntityFilters.Attributes)`. |
| `IDataverseRelationshipService` | Associate / Disassociate N:N. |
| `IChangesetApplier` | Staged operation applier with strategies `batch`, `transaction`, `bulk`. Operation types: CREATE/UPDATE/DELETE/ASSOCIATE/DISASSOCIATE/UPLOAD over targets entity/attribute/relationship/optionset/record/file. |
| `IDataverseFileService` | Chunked file column upload/download. |

### Critical gaps in txc services (must add for this plan)

- ❌ No `SetStateRequest` (statecode/statuscode change as standalone op).
- ❌ No `AssignRequest` (owner change as standalone op).
- ❌ No generic `Execute` / `OrganizationRequest` runner for Custom APIs / Custom Actions.
- ❌ No `ExecuteWorkflowRequest` / Power Automate trigger.
- ❌ No alternate-key resolution helper as a service (we have an HTTP endpoint only).
- ❌ No CMT `data_schema.xml` generator.
- ❌ No XLSX template generator.
- ❌ No package validator.
- ❌ No "desired-state diff" engine (changeset is declarative but operations are pre-built by callers).

## CMT engine — what's actually there

Decompiled at `/Users/tomasprokop/Desktop/Repos/jan.hajek-PPToolsChangeTracking/decompiled/Microsoft.CrmSdk.XrmTooling.ConfigurationMigration.Wpf/`.

### Capabilities (verified by code inspection)

| # | Capability | Verdict | Evidence |
|---|---|---|---|
| 1 | Programmatic schema generation | Partial (WPF-coupled) | `SchemaGenerator.cs` (62KB) and `SchemaGeneratorViewModel.cs` exist but are tied to the WPF UI. Not consumable as a library from .NET on macOS/Linux. |
| 2 | Metadata-driven default field selection | ❌ | No evidence. |
| 3 | Lookup resolution by alternate keys | ❌ | `ImportCrmDataHandler.cs` only matches by GUID then `primarynamefield`. |
| 4 | Pre/post-import hooks | ❌ | No `IExtension`, no events. |
| 5 | Custom API / workflow execution | ❌ | Only plugin disable/enable (`disableplugins`). |
| 6 | BPF stage manipulation | ❌ | No `activestageid` / `businessprocessflowid` handling. |
| 7 | Owner Assign | ✅ | `ImportCrmEntityActions.AssignEntityToUser()` + `AssignRequest`. |
| 8 | statecode/statuscode | ✅ | `ImportCommonMethods.GetStatusAndState()` with reprocessing. |
| 9 | True idempotency | ❌ | `enabledDuplicateDetection: false` hard-coded; re-runs rely on GUID match. |
| 10 | XLSX support | ❌ | No `OpenXml` / `Excel` references anywhere in CMT. |
| 11 | Package extensibility (Package Deployer style) | ❌ | Package Deployer is a separate tool; no built-in template combines CMT data + custom code. |

### Implication

**Anything beyond #7, #8, and the basic CMT import/export must be implemented in txc.** The CMT engine is a black box we cannot extend; we treat it as a record-CRUD primitive.

The chosen architecture is therefore:

```
txc data package import <pkg>
  ├─ Phase A: legacy CMT engine (CmtImportRunner)        — existing; idempotent on GUID
  └─ Phase B: txc post-processor (new)                    — reads sidecars, applies non-CMT ops
       ├─ alternate-key resolution            (data_keys.xml)
       ├─ statecode/statuscode normalization  (data_state.xml)    (overlaps CMT; sidecar wins)
       ├─ owner assignments                   (data_owners.xml)   (overlaps CMT; sidecar wins)
       ├─ BPF stage moves                     (data_bpf.xml)
       └─ custom API / workflow calls         (data_actions.xml)
```

## Constraints

- Must run on macOS / Linux / Windows under modern .NET (matches existing `txc` runtime). CMT runs in a subprocess via `LegacyAssemblyHostSubprocess` to keep WPF-y deps out of the main CLI process.
- Must respect repo conventions: `OutputWriter` for results, `ILogger` for diagnostics, ASCII-only CLI output, `[CliIdempotent]` / `[CliDestructive]` attributes correctly applied, `TxcOutputJsonOptions.Default` for stdout JSON.
- Public CLI surface should be discoverable: `txc data package <verb>`.
