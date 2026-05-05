# Cross-cutting — Final CLI Command Surface

This document is a UX summary across all milestones — useful for review and for keeping the documentation honest. Not a separate milestone.

## `txc data package`

| Command | Status | Milestone | Purpose |
|---|---|---|---|
| `import <path>` | Existing → extended | M4 | Imports CMT package; if `txc-package.xml` exists, also runs Phase B sidecars. |
| `export` | Existing | — | Unchanged. |
| `convert` | Existing → schema-aware + key-aware | M2, M5 | XLSX → CMT data.xml using a schema and (optionally) alt-keys. |
| `generate-schema` | NEW | M1 | Generate `data_schema.xml` from a list of tables / a solution. |
| `generate-xlsx` | NEW | M2 | Generate Excel template from `data_schema.xml`. |
| `validate <path>` | NEW (deferred) | M3+ | Cross-check manifest, sidecars, schema, and (optionally) target env. |

### New `import` flags (M4)

```
--skip-postimport
--postimport-only
--continue-on-error
--dry-run
--report <path>
```

### New `convert` flags (M2, M5)

```
--schema <path>          required
--keys <path>            enables deterministic-id rewriting
--on-error <fail|skip-row|warn>
--no-schema              deprecated, one-release back-compat shim
```

## `txc data record` (new sub-tree, M4 + M5)

| Command | Milestone | Purpose |
|---|---|---|
| `assign --entity X --id … --owner …` | M4 | Wraps `IDataverseAssignmentService`. |
| `set-state --entity X --id … --state … --status …` | M4 | Wraps `IDataverseStateService`. |
| `set-bpf --entity X --id … --process … --stage …` | M4 | Wraps `IDataverseProcessService`. |
| `execute-action --name … [--bind …] [--param …]` | M4 | Wraps `IDataverseExecutionService` for Custom API/Action. |
| `execute-workflow --workflow … --id …` | M4 | Wraps `IDataverseWorkflowService`. |
| `resolve-key --entity X --key K --value V` | M5 | Wraps `IRecordKeyService.ResolveAsync`. |

These are the same primitives used by Phase B; exposing them at the CLI keeps "scripted single-record" use cases in the same toolbox and makes Phase B testable end-to-end.

## Headless / CI ergonomics

- All non-read commands honor `--yes` (via `[CliDestructive]` where applicable) and the existing headless guard.
- All emit JSON to stdout via `OutputWriter` (using `TxcOutputJsonOptions.Default`); diagnostics go to stderr via `ILogger`.
- `--report <path>` is consistent across `import` and `convert` so pipelines can collect artifacts uniformly.

## Documentation impact

- `docs/configuration-migration.md` — extend with new commands and "Sidecars" section.
- `docs/configuration-migration-sidecars.md` — NEW: sidecar reference (M3 lays it out).
- `docs/data-migration-runtime.md` — NEW: Phase B / state-machine runtime (M4 lays it out).
- README "Example Usage" — add an end-to-end migration walkthrough.

## Telemetry / output contract

No new contracts; we extend existing `OutputWriter.WriteResult` shapes:

- `import` adds a `postImport` block alongside the existing CMT result block.
- `generate-schema` / `generate-xlsx` follow the same `{ status, output, summary }` shape used by `convert` today.

## Open questions to resolve during M1 kickoff

- Should `generate-schema` accept multiple `--tables-file` inputs and merge? (probably yes)
- For BPF resolution, do we always want the *latest* version of a process, or pin by `versionnumber`? (default: latest; pin via attribute when needed)
- Do we need a "force re-run actions" flag for M4 even when probes say no-op? (probably yes — `--force-actions`)

These are left open; they don't change the architecture and can be settled in implementation PRs.
