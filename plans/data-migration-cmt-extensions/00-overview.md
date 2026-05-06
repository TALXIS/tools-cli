# Data Migration & CMT Extensions — Overview

> Branch: `feat/data-migration-cmt-extensions`
> Status: Planning (revised)
> Owner: Data tooling

## Problem statement

`txc data package` today is a thin wrapper around Microsoft's Configuration Migration Tool (CMT). It exports/imports `data.xml` + `data_schema.xml`, supports parallel connections, batch mode, and file columns, and has a primitive XLSX → `data.xml` converter.

CMT's engine is, however, **insufficient** for real-world Dataverse migrations from legacy systems. Concretely, the decompiled CMT shows:

- ❌ No programmatic schema generation (the GUI's `SchemaGenerator` is WPF-coupled).
- ❌ No metadata-driven default field selection.
- ❌ No alternate-key resolution — only GUID + `primarynamefield` fallback.
- ❌ Dedup is hard-disabled (`enabledDuplicateDetection: false`); re-imports rely on GUID match.
- ❌ No hooks: no pre/post-import, no per-record callbacks, no extensibility.
- ❌ No Custom API / Custom Action / workflow execution after import.
- ❌ No Business Process Flow (BPF) stage manipulation.
- ❌ No XLSX support of any kind.
- ✅ Owner Assign and `statecode`/`statuscode` ARE supported (good; we will reuse).
- ✅ Plugin bypass during import is supported (`disableplugins`).
- ✅ **2-pass import** handles cyclic FKs (create without FK → update FK in reprocess pass).
- ✅ **Annotations supported natively** (base64 `documentbody` inline in data.xml).

Real migrations from legacy systems need:

1. **Excel-driven authoring** — a staging Excel with data validation, FK dropdowns, and optionset dropdowns that serves as both a review artifact for business users and a machine-readable input for CMT conversion.
2. **State-machine operations** post-import — Custom APIs, BPF stages, workflows, owners, statecodes.
3. **Idempotent re-runs** with relationship resolution by alternate keys (not just GUID).

### Phase 1 Scope

txc owns **D + E + F** of the migration pipeline:
- **D**: Generate staging Excel from Dataverse metadata (schema-driven, with reference data)
- **E**: Convert filled staging Excel → CMT package + sidecars
- **F**: Import CMT package + run post-import operations

Upstream (extraction, enrichment, mapping) stays in Python for Phase 1. The **staging Excel is the contract** between the upstream tool and txc.

### Real scenarios informing design

- **SVS migration** (761 projects from MongoDB → Dataverse): custom API calls, BPF advancement, task history as annotations, deterministic GUIDs, 4 enrichment sources
- **CETIN-GF migration** (355 projects from 25+ Synapse DW tables → 6 Dataverse entities): cyclic FKs, 3 BPF types, 15+ lookup tables, SQL-based mappings, MSCRMCallerID impersonation, termin auto-creation + date patching

See `09-research-findings.md` for detailed analysis of both scenarios and CMT internals.

## Design principles

- **CMT artifacts stay canonical.** `data.xml` and `data_schema.xml` remain the source-of-truth, untouched and tool-compatible (Microsoft GUI / `pac data` continue to work).
- **Staging Excel is the human interface.** Business users review/correct data in Excel. All entities (migrated and reference-only) use the same sheet format. FK dropdowns, optionset dropdowns, and cell-level validation provide guardrails.
- **Unified entity sheets.** No separate "lookup" sheets — every entity gets the same kind of sheet. A per-entity `include_in_cmt` flag controls whether records go into `data.xml`.
- **Sidecar files** carry txc-specific concerns (post-import ops, owners, BPF, alternate keys, custom APIs) alongside the CMT XMLs.
- **Two execution layers**:
  - *Legacy CMT engine* (existing `CmtImportRunner`) handles record CRUD, file columns, dedup-by-GUID/primaryname, owner Assign, statecode.
  - *txc post-processor* (new) reads sidecars and applies non-CMT operations using existing `IDataverseRecordService`, `IChangesetApplier`, plus new services (Execute / SetState / Assign / BPF).
- **No new DSL.** Sidecars are plain XML mirroring CMT's style. A single package manifest (`txc-package.xml`) declares which sidecars apply, in what order.
- **Declarative first, imperative escape hatch.** 90% of post-import operations expressed as declarative sidecars. Code-based `PackageExtension` hooks for the remaining 10%.
- **Reproducible.** Re-importing the same package converges to the same state (idempotency via deterministic GUIDs + sidecar probes).
- **Source-control friendly.** All artifacts are diff-able, mergeable text files versioned alongside the solution.

## Scope summary

### In scope (this branch)

| Milestone | Theme | File |
|---|---|---|
| M0 | ClosedXML feature spike | (spike, no plan file) |
| M1 | CMT schema generation from metadata / solution | `02-m1-schema-generation.md` |
| M2a | Staging Excel template generation (unified entity sheets) | `03-m2-staging-excel.md` |
| M2b | Reference data loading into staging Excel | `03-m2-staging-excel.md` |
| M2c | Excel library integration (ClosedXML) | `03-m2-staging-excel.md` |
| M3 | Sidecar XML formats and package manifest | `04-m3-sidecar-formats.md` |
| M4 | Staging Excel → CMT package conversion | `05-m4-xlsx-to-cmt-conversion.md` |
| M4b | CMT package → staging Excel (reverse conversion) | `06-m4b-cmt-to-xlsx-conversion.md` |
| M5 | Import runtime + post-import operations | `07-m5-import-runtime.md` |
| M5b | Imperative extensibility hooks (deferred) | `07-m5-import-runtime.md` |
| M6 | Post-import state validation | `08-m6-validate-state.md` |

Plus:
- `01-assessment.md` — existing codebase state
- `09-research-findings.md` — CMT internals, KingswaySoft features, real scenario analysis
- `10-handoff.md` — developer onboarding
- `11-architecture-options.md` — option placement, CMT boundary, inheritance chain, docs structure

### Explicitly out of scope (future)

- Live BPF *definition* editing (we only move records *between* defined stages).
- A new orchestration DSL (we extend CMT format, we don't replace it).
- Full Package Deployer hosting / wrapping.
- Replacing CMT's import engine.
- UI / web tooling.
- Mapping engine in txc (future: extraction, enrichment, mapping stay in Python for Phase 1).
- Mapping tests, transformation functions, data maps, and business-context capture; those remain upstream AgenticETL/Python concerns in Phase 1.
- Migration waves, wave status, and cross-wave integrity checks.
- Load journal and rollback automation. Phase 1 aims for idempotent forward re-runs, not automatic undo.
- Source lineage catalog, coverage analysis, and structured Q&A workflow. Phase 1 may carry optional lineage fields, but it does not become the governance workspace.
- Delta/change tracking execution model. Row checksums support later delta detection but do not define a full delta sync.
- Generic connector capability abstraction; this plan is Dataverse/CMT-specific.
- MSBuild SDK project type for data migration (future: `tools-devkit-build`).
- Connector implementations loaded by txc (future: new repo).

## High-level command surface (target state)

```
txc data package generate-schema   --tables ... [--from-solution ...] -o data_schema.xml
txc data package generate-xlsx     --schema data_schema.xml -o template.xlsx
                                   [--include-apis <api1,api2>]
                                   [--include-bpf]
                                   [--profile <name>]
txc data package load-xlsx-refs    --workbook template.xlsx --profile <name>
                                   [--entities <comma-list>]
txc data package convert           --input template.xlsx --schema ... -o <dir>
txc data package export-xlsx       --package <dir> -o review.xlsx          # NEW: reverse
txc data package validate-state    --package <dir> --profile <name>        # NEW: post-import validation
txc data package export            --schema data_schema.xml -o ...         # existing
txc data package import            <package-path>                          # existing, extended
```

A package on disk looks like:

```
my-migration/
├── data.xml                  # CMT data (canonical, MS-compatible)
├── data_schema.xml           # CMT schema (canonical, MS-compatible)
├── [Content_Types].xml       # CMT (existing)
├── files/                    # CMT file columns (existing)
├── txc-package.xml           # NEW: manifest binding sidecars to package
├── data_keys.xml             # NEW: alternate-key declarations (M5)
├── data_owners.xml           # NEW: owner assignments (M5)
├── data_state.xml            # NEW: statecode/statuscode overrides (M5)
├── data_bpf.xml              # NEW: BPF stage moves (M5)
├── data_actions.xml          # NEW: Custom API / workflow / action calls (M5)
├── data_callerid.xml         # NEW: per-record MSCRMCallerID impersonation (M5)
└── data_postimport.xml       # NEW: ordered references to the above (M5)
```

## Reading order

1. `10-handoff.md` — **start here if you are a developer picking this up.** Original goals, decisions already made, repo conventions, source-file pointers, glossary.
2. `09-research-findings.md` — CMT internals research, real scenario analysis (SVS + CETIN-GF).
3. `01-assessment.md` — what the codebase + CMT have today.
4. `02` … `08` — milestone designs (each independently reviewable, one milestone per file).

## Tracking

Operational task tracking lives in the session SQL store (todos table). Each milestone has its own todo set; cross-milestone dependencies are recorded in `todo_deps`.
