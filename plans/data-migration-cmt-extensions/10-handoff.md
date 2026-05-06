# Developer Handoff

> Read this first if you are picking up the work without seeing the planning conversation.

## 1. Original Goals from the Requester

The plan was authored to address three concrete goals expressed by the requester (data tooling stakeholder):

1. **Excel-driven authoring.** Select tables, extract relevant columns, generate a CMT schema and an Excel template; business users fill the template; convert it back into CMT data format and import via the CLI.
2. **Post-import "state machine" operations.** Real migrations from legacy systems also need: calling Custom APIs after import, moving Business Process Flow (BPF) records into the right stage, running Dataverse classic workflows / Power Automate flows, and setting record owners and state.
3. **Idempotent re-runs with relationship resolution.** Migrations are run repeatedly; we need natural-key (alternate-key) resolution and convergent re-imports.

### New Goals (added during planning)

4. **Reverse Conversion** — Convert existing CMT packages back into staging Excel for review, editing, and re-import (M4b).
5. **State Validation** — Verify that the live Dataverse environment matches the expected state after import (M6).

Mapping:

| Goal | Primary milestone(s) |
|---|---|
| 1 — Excel + schema | M1 (schema gen) + M2 (XLSX) |
| 2 — State-machine ops | M3 (sidecar formats) + M5 (runtime) |
| 3 — Idempotent re-runs | M5 (keys + idempotency) |
| 4 — Reverse conversion | M4b (CMT → Excel) |
| 5 — State validation | M6 (validate-state) |

## 2. Decisions Already Made (do not re-litigate without a reason)

These were settled through clarifying questions with the requester. Capture context before re-opening any of them.

- **CMT artifacts stay canonical.** `data.xml` and `data_schema.xml` remain the source-of-truth and stay byte-compatible with the Microsoft Configuration Migration Tool / `pac data`. We do **not** modify them in place to add txc-specific concerns.
- **Sidecar files**, not inline edits, carry non-CMT concerns (owners, state, BPF, custom APIs, alt-keys, impersonation). A single `txc-package.xml` manifest binds them to a CMT package. See `04-m3-sidecar-formats.md`.
- **Unified entity sheets.** No separate "lookup" sheets — every entity (migrated + reference-only) gets its own sheet with the same layout. The only difference is the `include_in_cmt` flag.
- **Composite dropdowns.** "Label [value]" for optionsets, "Name [GUID]" for lookups — human-readable + parseable.
- **ClosedXML library.** MIT license, wraps OpenXml SDK, friendlier API for formatting/validation/named ranges.
- **No hidden sheets.** Full transparency — all sheets visible, metadata in `_meta` sheet (not veryHidden).
- **Declarative-first.** Sidecar XML files drive post-import operations; imperative hooks (PackageExtension) deferred.
- **Display names for BPF.** Stage GUIDs differ between environments; use display names, resolve at import time.
- **Named ranges (not INDIRECT).** INDIRECT verified broken for data validation dropdowns in spike.
- **Deterministic GUIDs.** MD5 hash of entity name + alternate key values — matches existing txc HTTP endpoint.
- **XLSX round-trip.** Schema → template → fill → convert back to `data.xml`. Reverse direction (data.xml → XLSX) added as M4b.
- **CMT engine is a black box.** It runs in a subprocess via `LegacyAssemblyHostSubprocess`. We do not patch it. All extensions sit alongside it (Phase B post-processor).
- **No new DSL.** Sidecars are XML in the same style as CMT files.
- **Post-import bypass defaults are conservative.** Bypass headers are available, but Phase B steps should default to normal Dataverse business logic unless the package, entity, or step explicitly opts in.

## 3. Important Pointers in *This* Repo (`TALXIS/tools-cli`)

Read these before starting any milestone.

### CLI Command Base Classes / Conventions

- `src/TALXIS.CLI.Core/Shared/TxcLeafCommand.cs` — base for leaf commands; enforces destructive prompts, headless `--yes`, profiling.
- `src/TALXIS.CLI.Core/Shared/ProfiledCliCommand.cs` — base for commands that connect to Dataverse via a profile.
- `src/TALXIS.CLI.Core/Shared/OutputFormatter.cs` and `src/TALXIS.CLI.Shared/OutputWriter.cs` — write **command result data** (stdout). Use `OutputFormatter`. Do **not** call `OutputWriter` directly except inside renderer lambdas (analyzer TXC003 enforces this).
- `src/TALXIS.CLI.Core/Shared/TxcOutputJsonOptions.cs` — JSON options for stdout JSON (camelCase, kebab-case enums, indented, ignore nulls).
- `src/TALXIS.CLI.Logging/TxcLoggerFactory.cs` — `TXC_LOG_FORMAT=json` (or stdout redirected) routes ILogger to stderr-JSON.
- Attributes: `[CliReadOnly]`, `[CliIdempotent]`, `[CliDestructive]` (+ `IDestructiveCommand`), `[CliWorkflow(...)]`, `[CliLongRunning]`. Apply correctly per command.

### Existing Data Services and Commands (touch points for the milestones)

- `src/TALXIS.CLI.Features.Data/DataPackageImportCliCommand.cs` — Phase A entry point; M5 will add Phase B post-processing here.
- `src/TALXIS.CLI.Features.Data/DataPackageExportCliCommand.cs`
- `src/TALXIS.CLI.Features.Data/DataPackageConvertCliCommand.cs` — naïve XLSX converter; replaced by `XlsxToCmtConverter` (M2/M4) and key-aware rewriter (M5).
- `src/TALXIS.CLI.Features.Data/DataModelConvertCliCommand.cs` — solution → DBML/SQL/EDMX. Useful precedent for "schema in, artifact out".
- `src/TALXIS.CLI.Features.Data/TransformCliCommand.cs` — already exposes `/ComputePrimaryKey`; M5 promotes it into a shared `IRecordKeyService`.
- `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseDataPackageService.cs` — bridge to CMT subprocess.
- `src/TALXIS.CLI.Platform.Xrm/CmtImportRunner.cs` and `CmtExportRunner.cs` — the actual subprocess runners. Treat as Phase A primitives.
- `src/TALXIS.CLI.Platform.Dataverse.Data/` — `IDataverseQueryService`, `IDataverseRecordService`, `IDataverseBulkService`, `IDataverseEntityMetadataService`, `IDataverseRelationshipService`, `IDataverseFileService`. M5 adds Assignment / State / Process / Execution / Workflow services into the same area (`Application/Services/...`).
- `src/TALXIS.CLI.Platform.Dataverse.Application/Extensions/DataverseApplicationServiceCollectionExtensions.cs` — DI registrations.
- `src/TALXIS.CLI.Analyzers/MustNotCallOutputWriterAnalyzer.cs` — TXC003 rule referenced above.

### Existing Docs to Extend

- `docs/configuration-migration.md` — primary CMT doc; M1, M2, M5 will extend it.
- `docs/configuration-migration-sidecars.md` — NEW (M3 creates).
- `docs/data-migration-runtime.md` — NEW (M5 creates).

### New Paths (created by milestones)

| Path                                                  | Description                                      |
|-------------------------------------------------------|--------------------------------------------------|
| `src/TALXIS.CLI.Features.Data/SchemaGeneration/`      | M1: Schema generation from Dataverse metadata    |
| `src/TALXIS.CLI.Features.Data/Xlsx/`                  | M2/M4/M4b: Excel generation, reading, conversion |
| `src/TALXIS.CLI.Features.Data/Package/Sidecars/`      | M3: Sidecar XML POCOs + readers/writers          |
| `src/TALXIS.CLI.Features.Data/Package/PostImport/`    | M5: Post-import runner + steps                   |
| `src/TALXIS.CLI.Features.Data/Package/Keys/`          | M5: Alternate key resolution + GUID synthesis    |
| `tests/Fixtures/`                                     | Test fixture files (schemas, packages, Excel)    |
| `tests/Fixtures/native-excel-export-sample.xlsx`      | Native Dataverse Excel export for reference       |

## 4. Reference Repos (research artifacts)

- **Decompiled CMT (latest):** `/Users/tomasprokop/Desktop/Repos/jan.hajek-PPToolsChangeTracking/decompiled/Microsoft.CrmSdk.XrmTooling.ConfigurationMigration.Wpf/DataMigrationUtility/`
  Key files referenced during research (for sanity-checks):
  - `DataMigrationUtility/SchemaGenerator.cs` (62 KB, WPF-coupled — confirms why we must re-implement schema gen).
  - `DataMigrationUtility/ImportCrmDataHandler.cs` — match-by-GUID + `primarynamefield` fallback; no alt keys.
  - `DataMigrationUtility/ImportCrmEntityActions.cs` — owner Assign + plugin disable.
  - `DataMigrationUtility/ImportCommonMethods.cs` — `GetStatusAndState()`.
  - Search for `enabledDuplicateDetection` to see the hard-coded false.
- **Decompiled Dataverse 9.2 server / tools:** `/Users/tomasprokop/Desktop/Repos/D365CE-server-9.2.25063` (consult only when SDK signature is unclear).
- **Microsoft Learn** — primary spec for `SetStateRequest`, `AssignRequest`, `RetrieveProcessInstancesRequest`, `SetProcessRequest`, `ExecuteWorkflowRequest`, BPF entity model. Always link the Learn page in the implementation PR.

## 5. ClosedXML Spike Findings

Key findings from the ClosedXML spike (detailed in `09-research-findings.md`):

1. **No INDIRECT for dropdowns.** `INDIRECT()` in data validation lists is silently ignored by Excel in some scenarios. Always use named ranges for cross-sheet dropdown validation.

2. **No double `CreateDataValidation()` on the same range.** Calling `CreateDataValidation()` twice on the same cell range creates an orphaned `sqref=""` node in the underlying XML, which corrupts the workbook. Always check for existing validations before adding new ones.

3. **32K item limit for data validation lists.** Excel's dropdown list validation supports a maximum of 32,768 items. If a reference entity exceeds this limit, the `generate-xlsx` command must fail with a clear error message.

## 6. Native Dataverse Export Analysis

Analysis of the native Dataverse Excel export format (sample in `tests/Fixtures/native-excel-export-sample.xlsx`):

| Feature                     | Native Export                        | Our Approach                          |
|-----------------------------|--------------------------------------|---------------------------------------|
| Row checksums               | Present (for conflict detection)     | Present (SHA-256 base64 in column B)  |
| Metadata sheet              | `veryHidden` (not visible to users)  | Visible `_meta` sheet (transparency)  |
| Lookup resolution           | By display name                      | By display name (composite format)    |
| Optionset display           | Label only                           | Composite "Label [value]"             |
| Data validation             | Basic                                | Full typed validation per field       |

## 7. Repo-Level Conventions (quick reference)

- **ASCII-only CLI output.** No emojis, no unicode icons, no box-drawing in command output. Use words like `OK` / `FAILED` / `STUCK`. Plan/markdown files may use unicode (existing precedent).
- **Stdout = result data, stderr = diagnostics.** `OutputFormatter`/`OutputWriter` for results; `ILogger` for progress, warnings, errors.
- **JSON output options:**
  - Command result JSON: `TxcOutputJsonOptions.Default`.
  - Config JSON: `TxcJsonOptions.Default` (camelCase, kebab-case enums, trailing commas, SecretRef converter).
- **Destructive commands** must declare `[CliDestructive]` and implement `IDestructiveCommand` (exposes `--yes`; required in headless).
- **OData paths to Dataverse** must escape `$select`/`$filter`/`$orderby` values via `Uri.EscapeDataString`.
- **Dataverse MSAL scope suffix is `//.default`** (note the double slash) — see `DataverseScope.DefaultSuffix`.
- **Environment-management settings** flow through `IEnvironmentSettingsService` returning `EnvironmentSetting` (flat key/value).
- **Run analyzers and existing tests after each change.** Do not add new linters/build/test infra for this work.

## 8. Reading Order

For a developer new to this project, the recommended reading order is:

1. **`10-handoff.md`** (this file) — context, decisions, and orientation
2. **`09-research-findings.md`** — spike results and technical deep-dives
3. **`01-assessment.md`** — problem statement and requirements
4. **`02-m1-schema-generation.md`** through **`08-m6-validate-state.md`** — milestone specifications

## 9. Glossary

| Term | Meaning |
|---|---|
| CMT | Microsoft Configuration Migration Tool — exports/imports `data.xml` + `data_schema.xml`. |
| Phase A | Existing CMT import via `CmtImportRunner` subprocess. |
| Phase B | New in-process post-processor that runs sidecar operations (M5). |
| Sidecar | An XML file alongside the CMT package that carries non-CMT operations. |
| Manifest | `txc-package.xml`; the only file Phase B must read first; binds sidecars. |
| Alt key | Alternate key — one or more fields used to identify a record by natural value. txc resolves these via FetchXML; no physical Dataverse alternate key required. |
| Mechanism A | Convert-time deterministic GUID synthesis from alt-key tuple (MD5). Lets CMT match by GUID across re-runs. |
| Mechanism B | Apply-time alt-key resolution via FetchXML for sidecars and lookup tokens. |
| BPF | Business Process Flow — a workflow record (`Category=4`) and a per-record `bpf_<entity>` row carrying `activestageid`. |
| Custom API / Custom Action | Dataverse-side operations invoked via generic `OrganizationRequest.Execute`. |
| Probe | Optional FetchXML in `data_actions.xml` that gates whether a `<call>` or `<execute>` runs (for idempotency). |
| Staging Excel | The typed, validated Excel workbook that serves as the data authoring interface. |
| Composite dropdown | A data validation dropdown showing "Label [value]" or "Name [GUID]" — human-readable + parseable. |
| Named range | An Excel named range pointing to a column, used for cross-sheet dropdown validation. |
| Reference-only entity | An entity included in the workbook for FK dropdown values but NOT included in CMT data.xml. |
| `include_in_cmt` | Per-entity flag: `true` = migrated (in data.xml), `false` = reference-only (dropdown source). |
| `traversedpath` | A BPF field containing the ordered list of stage GUIDs the record has passed through. |

## 10. Suggested Implementation Order

```
M1 (Schema Generation)
  │
  ├── M2c (ClosedXML Integration) ──→ M2a (Template Generation) ──→ M2b (Reference Data Loading)
  │
  ├── M3 (Sidecar Formats) ←── can be developed in parallel with M2
  │
  └── M4 (Excel → CMT Conversion) ──→ M4b (CMT → Excel Conversion) ──→ M5 (Import Runtime) ──→ M6 (State Validation)
```

Dependencies:
- M2c must precede M2a (need the library before generating workbooks)
- M2a must precede M2b (need the template before loading reference data)
- M3 can proceed in parallel with M2 (independent work streams)
- M4 depends on M2 + M3 (needs Excel reader + sidecar writers)
- M4b depends on M4 (reverse of forward conversion)
- M5 depends on M3 + M4 (needs sidecars + package format)
- M6 depends on M5 (validates the output of import)

## 11. End-to-End Test Workflow

The complete 9-step migration workflow that the test suite should validate end-to-end:

1. **Generate schema**: `txc data package generate-schema --tables Account,Contact,Opportunity -o data_schema.xml`
2. **Generate Excel template**: `txc data package generate-xlsx --schema data_schema.xml --profile dev -o migration.xlsx`
3. **Load reference data**: `txc data package load-xlsx-refs --workbook migration.xlsx --profile target-dev`
4. **Author data**: (manual step — fill in entity sheets in Excel)
5. **Convert to CMT package**: `txc data package convert --input migration.xlsx --schema data_schema.xml -o ./package/`
6. **Import**: `txc data package import --package ./package/ --profile target-dev`
7. **Validate state**: `txc data package validate-state --package ./package/ --profile target-dev`
8. **Export to Excel** (optional round-trip): `txc data package export-xlsx --package ./package/ -o review.xlsx --profile target-dev`
9. **Re-import** (if edits made): repeat steps 5–7

## 12. What is *Not* Yet Decided (open items)

These are intentionally left for the implementation PRs. They do not change architecture.

- Should `generate-schema` accept multiple `--tables-file` inputs and merge? (Probably yes.)
- BPF version pinning — default to latest, allow `versionnumber` pin attribute? (Probably yes.)
- A `--force-actions` flag for M5 even when probes say no-op? (Probably yes.)
- Whether to keep the `--no-schema` shim on `convert` after one release. (Removal target TBD.)
- Brownfield merge/key-map workflow when a natural key already exists in target under a different GUID. Phase 1 should fail by default and require an explicit operator decision.
- Load journal / rollback strategy. Idempotency supports safe re-runs, but it does not undo an import with structurally wrong keys.
- Migration waves and cross-wave integrity checks.
- Whether optional source-lineage metadata should stay as manifest/report-only metadata or become a first-class sidecar later.

## 13. Things Explicitly Out of Scope

- Replacing the CMT engine.
- Live BPF *definition* editing (we only move records between defined stages).
- Hosting Package Deployer.
- Any web/UI surface.
- Auto-creating stub records on Mechanism B miss (future, behind a flag).
- Mapping engine, mapping tests, data map, business Q&A workflow, coverage analysis, migration waves, rollback automation, and generic connectors.
