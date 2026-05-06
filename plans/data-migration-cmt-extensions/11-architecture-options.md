# Architecture — Option Placement & CMT Boundary

> This document defines WHERE each option/behavior is configured, the inheritance chain, and the boundary between what CMT handles internally vs what txc adds.

## Option Inheritance Chain

Options cascade with specificity — more specific wins over less specific:

```
CLI flag (runtime override)
  └─▶ Package manifest (txc-package.xml — package-wide defaults)
        └─▶ Per-entity (within manifest or sidecar — entity-specific override)
              └─▶ Per-record (within sidecar — record-specific override)
```

**Example**: `suppressPowerAutomateFlows` can be set:
1. **Package-wide** in manifest `<options>` → applies to all entities
2. **Per-entity** in manifest `<entityOptions>` → overrides package default for that entity
3. **CLI flag** `--suppress-flows` → runtime override for the whole run

### Manifest Structure (revised)

```xml
<txcPackage version="1">
  <cmt schema="data_schema.xml" data="data.xml" />
  
  <sidecars>
    <keys path="data_keys.xml" />
    <owners path="data_owners.xml" />
    <state path="data_state.xml" />
    <bpf path="data_bpf.xml" />
    <actions path="data_actions.xml" />
    <callerid path="data_callerid.xml" />
    <postimport path="data_postimport.xml" />
  </sidecars>
  
  <!-- Package-wide defaults -->
  <options>
    <suppressPowerAutomateFlows>true</suppressPowerAutomateFlows>
    <bypassBusinessLogic>CustomAsync</bypassBusinessLogic>
    <suppressDuplicateDetection>true</suppressDuplicateDetection>
    <autoDisassociate>false</autoDisassociate>
    <coalesceNonEmptyValues>false</coalesceNonEmptyValues>
    <continueOnError>false</continueOnError>
    <dryRun>false</dryRun>
  </options>
  
  <!-- Per-entity overrides (optional) -->
  <entityOptions>
    <entity name="account">
      <suppressPowerAutomateFlows>false</suppressPowerAutomateFlows>  <!-- keep flows active for accounts -->
      <coalesceNonEmptyValues>true</coalesceNonEmptyValues>           <!-- partial update for accounts -->
    </entity>
    <entity name="contact">
      <bypassBusinessLogic>none</bypassBusinessLogic>                 <!-- run all plugins for contacts -->
    </entity>
  </entityOptions>
</txcPackage>
```

### Resolution Logic

When processing entity `X`:
1. Start with package-wide `<options>` as defaults
2. If `<entityOptions>` has an entry for `X`, override matching values
3. If CLI flag is set, it wins over everything

Per-record options (callerid, owner, state) come from sidecars and don't participate in this cascade — they are always per-record.

---

## CMT Internal Capabilities — What We Do NOT Reimplement

These are handled by the CMT engine (Phase A). We document them, expose them via existing CLI flags, but don't add new txc code.

| Capability | How CMT Does It | txc Exposure |
|---|---|---|
| **Plugin step deactivation** | Queries `sdkmessageprocessingstep`, sets `statecode=1` (disabled) before import, re-enables after. Per-entity via `disableplugins` schema attribute. | Already exposed via schema `disableplugins` attribute |
| **Duplicate suppression** | Hard-codes `enabledDuplicateDetection: false` on all Create/Update calls | Automatic, no flag needed |
| **Owner assignment (user)** | Sets `CallerID = owninguser` GUID, creates record "as" that user. Falls back to base caller + AssignRequest on permission error. | Data includes `owninguser` field |
| **Owner assignment (team)** | Issues `AssignRequest` with team reference after create/update | Data includes `owningteam` field |
| **State/status management** | Strips statecode/statuscode from create payload, applies via `SetStateRequest` in reprocess pass. Special handling for incident, opportunity, quote, etc. | Data includes `statecode`/`statuscode` fields |
| **Lookup resolution** | GUID match first (in-memory cross-ref map), then CRM query by primary ID, then `primarynamefield` fallback | Data includes GUID-based entity references |
| **M:N relationship association** | `AssociateRequest` with pre-existing check. Handles reflexive relationships. | Schema includes `<relationships>` |
| **File column upload** | `InitializeFileBlocksUpload` → 4MB chunked upload → `CommitFileBlocksUpload` | `--export-files` flag |
| **Entity import ordering** | `businessunit → systemuser → team` first, then schema `entityImportOrder`, activities last, annotations/connections post-processing | Schema `entityImportOrder` element |
| **Batch mode / threading** | `ExecuteMultiple` batching, configurable batch size and parallel connections | `--batch-mode`, `--batch-size`, `--connection-count` |
| **Prefetch for dedup** | Pre-loads N records per entity for in-memory duplicate check | `--prefetch-limit` |
| **System entity handling** | Skips SYSTEM/INTEGRATION users, default teams, roles, field security profiles. Auto-resolves root BU. | Automatic |

### Key Nuance: CMT's `disableplugins` vs Modern Bypass Headers

CMT's approach to plugin bypass is **step deactivation** — it finds and disables SDK message processing steps at runtime, then re-enables them after import. This is:
- ✅ Effective
- ❌ Slow (requires discovering all steps)
- ❌ Risky (if import crashes, steps stay disabled)
- ❌ Doesn't bypass Power Automate flows or low-code plugins

txc adds **modern bypass headers** as an alternative/complement:
- `BypassCustomPluginExecution` — faster, no step management, requires privilege
- `MSCRM.BypassBusinessLogicExecution` — granular (async/sync)
- `MSCRM.SuppressCallbackRegistrationExpanderJob` — Power Automate bypass

These headers apply to Phase B (sidecar operations). Phase A (CMT engine) continues using its native step deactivation approach.

---

## What txc Adds ON TOP of CMT

These are features that CMT does not provide. txc implements them in Phase B (post-import) or as new commands.

| Feature | Where | Level |
|---|---|---|
| **Sidecar-driven post-import operations** | Phase B PostImportRunner | Per-entity, per-record (sidecars) |
| **BPF stage advancement** | `data_bpf.xml` sidecar | Per-record |
| **Custom API calls** | `data_actions.xml` sidecar | Per-record or global |
| **Per-record impersonation** | `data_callerid.xml` sidecar | Per-record |
| **Modern bypass headers** (Power Automate, granular plugins) | Manifest `<options>` + `<entityOptions>` | Package or per-entity |
| **Alternate key resolution** | `data_keys.xml` + deterministic GUIDs | Per-entity |
| **Staging Excel round-trip** | `generate-xlsx`, `load-xlsx-refs`, `convert`, `export-xlsx` | Commands |
| **Schema generation** | `generate-schema` | Command |
| **Structured error reporting** | Convert + import | Per-row |
| **Post-import state validation** | `validate-state` | Command |
| **CoalesceNonEmptyValues** | Manifest `<options>` / `<entityOptions>` | Package or per-entity |
| **AutoDisassociate** | Manifest `<options>` / `<entityOptions>` | Package or per-entity |
| **overriddencreatedon** | Schema field + data.xml field | Per-record |
| **createdby/modifiedby** | CallerID per record + schema field | Per-record |
| **Row checksums** | Staging Excel column B | Per-record |
| **Source duplicate detection** | Convert step | Per-entity |
| **Retry with Retry-After** | PostImportRunner | Runtime |

---

## Documentation Structure

New subfolder `docs/data-migration/` with granular topic files:

```
docs/
├── configuration-migration.md          # Existing (CMT core docs — keep, extend)
└── data-migration/
    ├── README.md                       # Overview + reading order
    ├── staging-excel.md                # Excel format spec, column types, styling
    ├── schema-generation.md            # generate-schema command + field selection
    ├── sidecar-formats.md              # All sidecar XML formats with examples
    ├── package-manifest.md             # txc-package.xml options + entity overrides
    ├── bypass-headers.md               # Plugin, Power Automate, dupe detection bypass
    ├── import-runtime.md               # Phase A + Phase B + retry + error handling
    ├── idempotency-and-keys.md         # Deterministic GUIDs, alternate keys
    ├── bpf-advancement.md              # BPF stage moves, traversedpath
    ├── state-validation.md             # validate-state command
    └── migration-walkthrough.md        # End-to-end 9-step example
```

Each file is self-contained, focused on one topic, and includes examples.
