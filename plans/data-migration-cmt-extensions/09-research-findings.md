# Research Findings — CMT Internals & Real Migration Scenarios

> This file captures research performed against the decompiled CMT source code and two real migration prototypes (SVS and CETIN-GF). Findings inform design decisions across all milestones.

## CMT Engine Internals (verified from decompiled source)

### 2-Pass Import for Cyclic FKs

CMT uses a **two-pass architecture** to handle foreign key cycles:

1. **Pass 1 (Import):** Entities are imported in `entityImportOrder`. When a lookup field references a record that hasn't been imported yet, the field is **stripped from the create request** and the record+field is added to a reprocessing list. The record is still created (without the unresolvable FK fields).

2. **Pass 2 (Reprocess):** After all entities are imported, CMT iterates the reprocessing list and issues `Update` requests to patch in the FK values (now that target records exist).

3. **Pass 3 (M2M):** Many-to-many relationships are created last.

**Limitation:** Only **one** reprocess pass. If a lookup still can't be resolved during reprocess (e.g., a 3-entity cycle where resolution order is unlucky), the field update fails with a warning. The `entityImportOrder` in the schema is the user's mechanism to mitigate this.

**Implication for M1:** Schema generator's topological sort is critical. Correct `entityImportOrder` prevents most cyclic FK issues. CETIN-GF's manual 2-phase CMT zip splitting is unnecessary — CMT handles it internally.

### Annotation Support

CMT **fully supports annotation records** (notes with attachments):
- Annotations are regular entity records in `data.xml` — no special XML format
- `documentbody` (base64 file content) is force-included via an explicit exception in schema generation code (`IsExecptionToRule()`)
- `filename` and `mimetype` are standard string fields
- `objectid` (parent reference) is a standard lookup field
- Annotations are force-ordered to import **last** (after all parent records exist)
- Attachment content is stored **inline as base64** (not as external files like the newer `filedata`/File column mechanism)

**Implication:** No special txc handling needed for notes/annotations. Include the `annotation` entity in the staging Excel like any other entity.

### Entity Import Ordering

CMT's `ForceOrderOfSomeEntities()` enforces:
- **Always first:** `businessunit`, `systemuser`, `team`
- **User-defined:** `entityImportOrder` from schema XML (overrides defaults when present)
- **Always last:** `metric`, `goal`, `annotation`, `activitymimeattachment`, `connection`, `postfollow`, `post`
- **Activity entities** (by ETC) come after regular entities but before "always last"

### Owner Assign & State/Status

CMT handles these natively:
- `ImportCrmEntityActions.AssignEntityToUser()` + `AssignRequest` for owner changes
- `ImportCommonMethods.GetStatusAndState()` with reprocessing for statecode/statuscode
- Sidecar overrides (`data_owners.xml`, `data_state.xml`) are only needed when CMT's behavior is insufficient (e.g., more complex resolution, conditional assignment)

### What CMT Does NOT Support

- ❌ Programmatic schema generation (WPF-coupled `SchemaGenerator.cs`)
- ❌ Metadata-driven field selection
- ❌ Alternate-key resolution (only GUID + `primarynamefield` fallback)
- ❌ Duplicate detection (hard-disabled: `enabledDuplicateDetection: false`)
- ❌ Pre/post-import hooks or extensibility
- ❌ Custom API / Custom Action / workflow execution
- ❌ BPF stage manipulation
- ❌ XLSX support of any kind

---

## Real Scenario Analysis

### SVS Migration (761 projects)

| Property | Value |
|---|---|
| Source | MongoDB export → Excel (2 sheets: Projekty + Tasky) |
| Target | Dataverse: `pba_project_evaluation`, `pba_project_evaluation_planning_data` |
| Enrichment | 4 sources joined by KIA: Production CRM, SharePoint Sites, IAD, Dataverse lookups |
| Key patterns | Deterministic GUIDs (UUID5 from svsId), optionset label matching, 3-tier okres fallback, dual-key user resolution |
| Post-import | `pba_CreateTask` custom API (U1-U8 codes), `pba_SwitchBPFStage` (stages 101-105), task history JSON as annotations, link active task |
| Volume | 761 projects + 18,550 task records |

### CETIN-GF Migration (355 projects)

| Property | Value |
|---|---|
| Source | IPMS (Synapse DW, 20+ tables) + CRM (Synapse L0_CRM) + 2 Excel files |
| Target | Dataverse: 6 entities (`pba_hlavicka_zps`, `pba_projekt`, `pba_projekt_gf`, `pba_hlavicka_gf`, `pba_obecnaprojektu`, `pba_termin`) |
| Enrichment | 15+ Dataverse lookup tables + 5+ Synapse codelists + 2 Excel files |
| Key patterns | SQL-based mappings (~600 lines/entity), 7 user role lookups (IPMS ID → login → domainname), 11 monetary fields, Praha district special-case logic |
| Post-import | BPF advancement (3 BPF types: `pba_projektbpf`, `pba_projektgfrezi`, `pba_zpsbpf`), termin date PATCH after auto-creation, `MSCRMCallerID` impersonation, `overriddencreatedon` |
| Volume | 355 projects × 6 entities + ~3,550 milestones |
| FK complexity | Cyclic FK between `pba_projekt` ↔ `pba_projekt_gf` (handled by CMT's 2-pass) |

### Key Differences Driving Design

| Dimension | SVS | CETIN-GF | Design Implication |
|---|---|---|---|
| Lookup complexity | 4 YAML lookups | 15+ DV tables | Reference data loading must scale; formula-based validation up to 32K records |
| BPF | Custom API (`pba_SwitchBPFStage`) | Direct PATCH on BPF entities | Must support both declarative BPF sidecar and custom API sheets |
| Post-import | CreateTask API → link task | Termin auto-create → query → PATCH dates | Need "query-then-update" pattern in `data_actions.xml` |
| Impersonation | Not needed | `MSCRMCallerID` per-record | `data_callerid.xml` sidecar |
| Mapping language | Python functions | Pure SQL (DuckDB) | Out of scope for Phase 1; both produce the same staging Excel contract |

---

## Excel Library Assessment

### Current state
The codebase uses `DocumentFormat.OpenXml` v3.3.0 (Microsoft Open XML SDK). The existing `DataPackageConvertCliCommand` only **reads** XLSX (via `SpreadsheetDocument.Open` read-only); it does not generate workbooks.

### Python prototype features (must match)
The prototype uses openpyxl with: data validation (list dropdowns from sheet ranges), conditional formatting (cell-is + formula rules), cell comments, sheet protection (per-cell lock/unlock), frozen panes, auto-filter, hidden rows/columns, tab colors, formulas, column auto-fit.

### Recommendation: ClosedXML (MIT license)
- Wraps the same `DocumentFormat.OpenXml` we already depend on
- Much simpler API for data validation, styling, conditional formatting vs raw OpenXml
- MIT license — no commercial restrictions (unlike EPPlus which is LGPL/commercial post-v5)
- Active maintenance, good community
- **Spike required** before committing — verify INDIRECT-based data validation, conditional formatting, named ranges, sheet protection with per-cell unlock

---

## BPF Advancement API

Based on [Microsoft Learn documentation](https://learn.microsoft.com/en-us/power-automate/developer/business-process-flows-code):

- BPF definitions stored in `workflow` table (`Category=4`)
- Stage information in `processstage` table
- BPF instances are entity records (e.g., `pba_projektbpf`) with `activestageid` navigation property
- To set a non-first stage, must provide `traversedpath` (comma-delimited stage GUIDs from first to target)
- `SetProcess` message is deprecated for switching processes; use direct PATCH instead
- Stage GUIDs differ between environments — **resolve by display name at import time**

Resolution strategy for `data_bpf.xml`:
1. Process display name → `workflow.workflowid`
2. Stage display name → `processstage.processstageid` (via `processstages?$filter=processid/workflowid eq {id}`)
3. Auto-compute `traversedpath` from first stage to target stage
4. Create or update BPF instance with `activestageid@odata.bind` and `traversedpath`

---

## KingswaySoft Feature Analysis (from decompiled 2025 assembly)

> Source: `/Users/tomasprokop/Desktop/Experiments/KWSDecompilation/`
> The user considers SSIS + KingswaySoft to be the most productive tool for Dataverse migrations. This analysis extracts features that should inspire our CMT extensions.

### Lookup Resolution (most important KingswaySoft feature)

KingswaySoft has per-lookup-field, per-target-entity configuration:

| Setting | Purpose | Relevance to our plan |
|---|---|---|
| `TargetTextField` | Match by a text field (e.g., `name`, `fullname`) | Our composite `"Name [GUID]"` approach handles this |
| `AlternateKey` | Use a Dataverse alternate key for matching | Maps to our M5 Mechanism B |
| `SecondaryLookupInputColumn` + `SecondaryLookupTargetField` | Two-field composite lookup | **NEW** — consider for ambiguous lookups |
| `ExcludeInactive` | Skip inactive records when resolving | **Consider** — add `statecode eq 0` filter to lookup queries |
| `SmartNameMatch` | Fuzzy matching for names | **Future** — useful but complex |
| `DefaultValue` | Fallback GUID if lookup fails | Our plan has `--on-error skip-row` but not per-field fallback |
| `Nullify` | Set lookup to null if not found | **Consider** — useful for optional lookups |
| `LookupCacheStrategy` | `FullCache` (preload all) vs `PartialCache` (on-demand) | Our reference-only entity sheets = FullCache; large tables need PartialCache |

**Record Matching Criteria for Upsert:**
- `PrimaryKey` — match by GUID (CMT default)
- `AlternateKey` — match by Dataverse alternate key (our M5)
- `SystemDuplicateDetection` — reuse Dataverse's own dupe detection rules (interesting but complex)
- `ManuallySpecify` — pick matching fields (our `data_keys.xml`)

### Bypass Headers (critical for performance)

| Header | Purpose | In our plan? |
|---|---|---|
| `MSCRM.BypassCustomPluginExecution` | Skip custom plugins | ✅ Yes (`disableplugins` in schema) |
| `MSCRM.BypassBusinessLogicExecution` | Granular: `CustomAsync` (1) or `CustomSync` (2) | ⚠️ **NEW** — more granular than all-or-nothing |
| `MSCRM.BypassBusinessLogicExecutionStepIds` | Bypass specific plugin step GUIDs | ⚠️ **NEW** — surgical bypass |
| `MSCRM.SuppressCallbackRegistrationExpanderJob` | **Bypass Power Automate flows** | ❌ **Missing from plan** — critical for perf |
| `MSCRM.SuppressDuplicateDetection` | Skip duplicate detection rules | ✅ Yes |
| `MSCRMCallerID` | Per-record impersonation | ✅ Yes (`data_callerid.xml`) |
| `AutoDisassociate: true` | Auto-remove old N:1 associations on update | ⚠️ **NEW** — useful for reassigning lookups |

### Option Set Non-Match Strategies

| Strategy | What it does | In our plan? |
|---|---|---|
| `CreateOption` | Auto-create missing option set values | ❌ Not planned (risky but powerful) |
| `ReplaceAs` | Map to a specific fallback value | ❌ Not planned — consider as `--on-error` option |
| `SetAsEmpty` | Null it out | Partial (empty cells omitted from XML) |
| `LeaveItAsIs` | Pass through raw | ❌ |
| `RaiseAnError` | Fail the row | ✅ Default in our plan |

### Batch Operations — Three Tiers

| Tier | API | KingswaySoft default | Our plan |
|---|---|---|---|
| **Homogeneous** | `CreateMultiple` / `UpdateMultiple` / `UpsertMultiple` | Preferred (checkbox) | ✅ `IDataverseBulkService` has these |
| **Heterogeneous** | OData `$batch` with changesets | Fallback | ✅ `IChangesetApplier` |
| **Legacy** | `ExecuteMultiple` | Old approach | Available but not preferred |

KingswaySoft defaults: `BatchSizeForWriting=10`, `ConcurrentWritingThreadsInTotal=20` (= 200 effective concurrent ops).

### Error Handling — Row Redirection Pattern

KingswaySoft uses SSIS's error output pattern: failing rows are redirected to an error output stream with the error message attached. The original row data is preserved.

**Relevance**: Our `convert` command has `--on-error fail|skip-row|warn`. We should also emit a structured error report with the original row data + error per row, not just a count.

### Retry / Throttling

| Scenario | KingswaySoft retry | Our plan |
|---|---|---|
| HTTP 429 (Too Many Requests) | Wait `Retry-After` header, fallback 90s | Should implement |
| HTTP 503 (Service Unavailable) | Wait 90s | Should implement |
| SOAP throttling | Wait 150s | Should implement |
| SQL timeout (`-2147204784`) | Retry | Should implement |
| Specific Dataverse throttling codes | `-2147015903`, `-2147015902`, `-2147015898` | **Reference data** for our retry logic |

### Per-Record Impersonation (`impersonateas` field)

KingswaySoft supports a virtual `impersonateas` input column — each row can specify which user to impersonate via `MSCRMCallerID` header. This is more flexible than connection-level impersonation.

**Relevance**: Our `data_callerid.xml` sidecar achieves this. Consider also supporting `createdby`/`modifiedby` override (KingswaySoft can set these with batch size=1).

### Other Notable Features

| Feature | KingswaySoft | Impact on our plan |
|---|---|---|
| `CoalesceNonEmptyValues` | Only update non-empty fields (preserve existing data) | **Consider** for M5 — useful for partial updates |
| `overriddencreatedon` | Explicitly set historical creation dates | ✅ Our schema generator should include this field |
| Change Tracking / `$deltatoken` | Incremental extraction | **Future** — not Phase 1 |
| Source duplicate optimization | `WriteFirstOnly` / `WriteAllWithNoDuplicateCreation` | **Consider** — handle dupes in staging Excel |
| `statecode` required with `statuscode` | Can't set statuscode without statecode | ✅ Enforce in validation |
| Activity party handling | Dedicated struct with per-entity resolution | **Future** — complex but needed for activity migration |

### Features to Add to Plan

Based on this analysis, these features should be considered for plan updates:

1. **`MSCRM.SuppressCallbackRegistrationExpanderJob` header** — Power Automate bypass. Add to manifest options alongside `disableplugins`.
2. **Granular plugin bypass** — `BypassBusinessLogicExecution` (async-only vs sync) in manifest.
3. **`ExcludeInactive` for lookups** — filter reference data loading to active records only.
4. **Structured error report with original row data** — not just counts but the actual failing rows.
5. **Retry with Retry-After** for 429/503 — add to import runtime.
6. **`overriddencreatedon` as explicit schema field** — always include when mode=full.
7. **Per-field lookup fallback** (nullify vs error) — richer than current all-or-nothing `--on-error`.
