# M1 — Schema Generation

> Goal: Generate a Microsoft-compatible `data_schema.xml` programmatically — from a list of tables, from a Dataverse environment, or (later) from a local solution project.

## Why

Today users must hand-author `data_schema.xml` or capture it from the WPF Configuration Migration Tool (Windows-only, GUI). This is the single biggest blocker to "scripting" CMT in CI and to generating XLSX templates (M2) and sidecars (M3).

The decompiled `SchemaGenerator.cs` is WPF-coupled and not reusable. We re-implement schema generation against the public Dataverse SDK using the existing `IDataverseEntityMetadataService`.

## Output contract

The generated file MUST be byte-comparable, on the meaningful-content level, to a CMT GUI export — i.e. the WPF tool, `pac data`, and txc itself must all be able to consume it without modification.

Required structure (already documented in `docs/configuration-migration.md`):

- `<entities dateMode="absolute">`
- `<entityImportOrder>` ordered by FK dependencies (parents first).
- `<entity name=… etc=… primaryidfield=… primarynamefield=… disableplugins=…>` per table.
- `<fields>` with `name / displayname / type / lookupType / customfield / primaryKey`.
- `<relationships>` (one per relationship export choice).
- Optional `<filter>` with FetchXML.

## Field selection strategy

Three modes, chosen via CLI flag, all metadata-driven:

| Mode | Includes | Excludes | Use case |
|---|---|---|---|
| `minimal` | Primary key, primary name, custom fields, mandatory non-system fields | Audit fields, owner fields (handled via sidecar M4), state/status (sidecar), system fields | "Move only what the customer authored." |
| `standard` (default) | minimal + lookups + option-set columns | Audit, ownership, state, system | Most migrations. |
| `full` | All `IsValidForCreate=true && IsValidForUpdate=true` attributes | Truly read-only / system-managed (`createdon`, `modifiedon`, `versionnumber`, `*_base`, calculated/rollup) | Power users. |

In all modes:

- `IsCustomAttribute=true` → `customfield="true"`.
- `IsPrimaryId` → emit as `<field … primaryKey="true">`.
- Lookups → emit `lookupType="<targetEntity>"`. If multiple targets (Customer, Owner, regarding) → emit comma-separated, matching CMT GUI behavior.
- Calculated / Rollup / Formula → exclude (CMT cannot import).
- Multi-select picklist (`Virtual` of type `MultiSelectPicklist`) → include with `type="picklist"`.

## Entity import ordering

Topological sort over many-to-one relationships among the *selected* entities:

- Build a directed graph: edge `child -> parent` when `child.lookupAttr` references parent.
- Self-references (reflexive) → break with a warning; record needs a 2-pass create-then-update which CMT handles.
- Output the topologically-sorted list as `<entityImportOrder>`.

Tied components (e.g. cycle remnants) → emit deterministically by alphabetical logical name and log a warning.

## Relationships included

For each selected entity, include:

- All many-to-one relationships **whose parent entity is also selected**. Drop edges to non-selected entities (lookup will simply remain empty in target) with a `WARN`.
- Many-to-many relationships when **both** sides are selected — opt-in via `--include-m2m` flag (default off; M2M significantly increases payload).

## Inputs

```
txc data package generate-schema
  --tables <comma-list>             # OR
  --tables-file <path>              # newline-separated
  --from-solution <path>            # later (M1.b): infer from .cdsproj
  --fields <minimal|standard|full>  # default: standard
  --include-m2m                     # default: off
  --include-system                  # default: off (skip systemuser/team/businessunit unless explicit)
  --filter <table>=<fetchxml-file>  # repeatable, attaches a <filter> per table
  --output <path>                   # required
  --profile <name>                  # connect to source env for metadata
```

## Implementation outline

New project: nothing new — fits in `TALXIS.CLI.Features.Data` and a new internal namespace `TALXIS.CLI.Features.Data.SchemaGeneration`.

New types:

```
SchemaGeneration/
├── ISchemaGenerator.cs                  # contract
├── SchemaGenerator.cs                   # orchestrates: metadata fetch -> filter -> sort -> serialize
├── EntitySelection.cs                   # parsed CLI input
├── FieldSelectionMode.cs                # enum
├── FieldFilter.cs                       # rule set per mode
├── EntityOrderResolver.cs               # topological sort
└── SchemaXmlWriter.cs                   # XElement-based emitter
```

New CLI command: `DataPackageGenerateSchemaCliCommand` under `TALXIS.CLI.Features.Data`.
- Inherits `ProfiledCliCommand`.
- `[CliReadOnly]` (only reads metadata; writes a local file).
- `[CliWorkflow("local-development")]`.

Wire `ISchemaGenerator` into DI in `DataverseApplicationServiceCollectionExtensions` next to `IDataverseEntityMetadataService`.

## Validation

- Reject if any `--tables` entry is not present in target env (clear error).
- Reject if `--fields full` is paired with audit-heavy entities (warn + opt-in flag).
- Re-running with the same flags against the same env must produce a byte-identical file (deterministic ordering throughout; UTF-8 no-BOM; LF line endings; no timestamps).

## M1.b — From solution (deferred within milestone)

Read `.cdsproj` / declarations folder (already supported by `DataModelConverterService`) and pre-fill `--tables` from solution components. This is additive: if both `--from-solution` and `--tables` are given, `--tables` overrides.

## Tests

Add to `tests/TALXIS.CLI.Features.Data.Tests` (create if absent):

- Round-trip: generate against a test metadata fixture → serialize → re-parse → assert structural equality.
- Topological sort: synthetic graph with cycles, with multiple roots.
- Field filtering: each mode produces the expected set against a known entity metadata fixture.
- Determinism: two consecutive runs produce identical bytes.

## Done when

- `txc data package generate-schema --tables account,contact -o ./schema.xml` produces a file accepted by `txc data package export --schema ./schema.xml`.
- File round-trips through the Microsoft CMT GUI without warnings (manual smoke test).
- Documentation updated in `docs/configuration-migration.md`.
