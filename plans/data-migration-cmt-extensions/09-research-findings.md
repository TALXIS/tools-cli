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
