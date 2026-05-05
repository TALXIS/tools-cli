# Data Migration & CMT Extensions — Overview

> Branch: `feat/data-migration-cmt-extensions`
> Status: Planning
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

Real migrations from legacy systems need:

1. **Excel-driven authoring** — business users fill XLSX templates that round-trip with `data.xml`.
2. **State-machine operations** post-import — Custom APIs, BPF stages, workflows, owners, statecodes.
3. **Idempotent re-runs** with relationship resolution by alternate keys (not just GUID).

## Design principles

- **CMT artifacts stay canonical.** `data.xml` and `data_schema.xml` remain the source-of-truth, untouched and tool-compatible (Microsoft GUI / `pac data` continue to work).
- **Sidecar files** carry txc-specific concerns (post-import ops, owners, BPF, alternate keys) alongside the CMT XMLs.
- **Two execution layers**:
  - *Legacy CMT engine* (existing `CmtImportRunner`) handles record CRUD, file columns, dedup-by-GUID/primaryname, owner Assign, statecode.
  - *txc post-processor* (new) reads sidecars and applies non-CMT operations using existing `IDataverseRecordService`, `IChangesetApplier`, plus new services (Execute / SetState / Assign / BPF).
- **No new DSL.** Sidecars are plain XML mirroring CMT's style. A single package manifest (`txc-package.xml`) declares which sidecars apply, in what order.
- **Reproducible.** Re-importing the same package converges to the same state (idempotency).
- **Source-control friendly.** All artifacts are diff-able, mergeable text files versioned alongside the solution.

## Scope summary

### In scope (this branch)

| Milestone | Theme | File |
|---|---|---|
| M1 | CMT schema generation from metadata / solution | `02-schema-generation.md` |
| M2 | XLSX round-trip (template generation + improved convert) | `03-xlsx-roundtrip.md` |
| M3 | Sidecar XML formats and package manifest | `04-sidecar-formats.md` |
| M4 | State-machine runtime (post-import operations) | `05-state-machine-runtime.md` |
| M5 | Idempotency and alternate-key resolution | `06-idempotency-and-keys.md` |
| X-cut | Final CLI command surface | `07-cli-command-surface.md` |

Plus an assessment note for context: `01-assessment.md`.

### Explicitly out of scope (future)

- Live BPF *definition* editing (we only move records *between* defined stages).
- A new orchestration DSL (we extend CMT format, we don't replace it).
- Full Package Deployer hosting / wrapping.
- Replacing CMT's import engine.
- UI / web tooling.

## High-level command surface (target state)

```
txc data package generate-schema   --tables ... [--from-solution ...] -o data_schema.xml
txc data package generate-xlsx     --schema data_schema.xml -o template.xlsx
txc data package convert           --input template.xlsx -o data.xml      # existing, hardened
txc data package validate          <package-path>                          # new
txc data package export            --schema data_schema.xml -o ...         # existing
txc data package import            <package-path>                          # existing, extended to run sidecars
```

A package on disk looks like:

```
my-migration/
├── data.xml                  # CMT data (canonical, MS-compatible)
├── data_schema.xml           # CMT schema (canonical, MS-compatible)
├── content_types.xml         # CMT (existing)
├── files/                    # CMT file columns (existing)
├── txc-package.xml           # NEW: manifest binding sidecars to package
├── data_keys.xml             # NEW: alternate-key declarations (M5)
├── data_owners.xml           # NEW: owner assignments (M4)
├── data_state.xml            # NEW: statecode/statuscode overrides (M4)
├── data_bpf.xml              # NEW: BPF stage moves (M4)
├── data_actions.xml          # NEW: Custom API / workflow / action calls (M4)
└── data_postimport.xml       # NEW: ordered references to the above (M4)
```

## Reading order

1. `08-handoff.md` — **start here if you are a developer picking this up.** Original goals, decisions already made, repo conventions, source-file pointers, glossary.
2. `01-assessment.md` — what the codebase + CMT have today.
3. `02` … `06` — milestone designs (each independently reviewable).
4. `07-cli-command-surface.md` — final UX summary across milestones.

## Tracking

Operational task tracking lives in the session SQL store (todos table). Each milestone has its own todo set; cross-milestone dependencies are recorded in `todo_deps`.
