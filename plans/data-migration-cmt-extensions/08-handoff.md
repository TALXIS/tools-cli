# Developer Handoff

> Read this first if you are picking up the work without seeing the planning conversation.

## 1. Original goals from the requester

The plan was authored to address three concrete goals expressed by the requester (data tooling stakeholder):

1. **Excel-driven authoring.** Select tables, extract relevant columns, generate a CMT schema and an Excel template; business users fill the template; convert it back into CMT data format and import via the CLI.
2. **Post-import "state machine" operations.** Real migrations from legacy systems also need: calling Custom APIs after import, moving Business Process Flow (BPF) records into the right stage, running Dataverse classic workflows / Power Automate flows, and setting record owners and state.
3. **Idempotent re-runs with relationship resolution.** Migrations are run repeatedly; we need natural-key (alternate-key) resolution and convergent re-imports.

Mapping:

| Goal | Primary milestone(s) |
|---|---|
| 1 — Excel + schema | M1 (schema gen) + M2 (XLSX) |
| 2 — State-machine ops | M3 (sidecar formats) + M4 (runtime) |
| 3 — Idempotent re-runs | M5 (keys + idempotency) |

## 2. Decisions already made (do not re-litigate without a reason)

These were settled through clarifying questions with the requester. Capture context before re-opening any of them.

- **CMT artifacts stay canonical.** `data.xml` and `data_schema.xml` remain the source-of-truth and stay byte-compatible with the Microsoft Configuration Migration Tool / `pac data`. We do **not** modify them in place to add txc-specific concerns.
- **Sidecar files**, not inline edits, carry non-CMT concerns (owners, state, BPF, custom APIs, alt-keys). A single `txc-package.xml` manifest binds them to a CMT package. See `04-sidecar-formats.md`.
- **XLSX is a round-trip.** Schema → template → fill → convert back to `data.xml`. Reverse direction (data.xml → XLSX) is out of scope for the first cut.
- **CMT engine is a black box.** It runs in a subprocess via `LegacyAssemblyHostSubprocess`. We do not patch it. All extensions sit alongside it (Phase B post-processor).
- **No new DSL.** Sidecars are XML in the same style as CMT files.
- **Long-term direction (informational, not in scope here):** generate the CMT schema from a Dataverse solution project. M1 covers tables-list and env-driven generation; the solution-driven mode is "M1.b" deferred-within-milestone.

## 3. Important pointers in *this* repo (`TALXIS/tools-cli`)

Read these before starting any milestone.

### CLI command base classes / conventions

- `src/TALXIS.CLI.Core/Shared/TxcLeafCommand.cs` — base for leaf commands; enforces destructive prompts, headless `--yes`, profiling.
- `src/TALXIS.CLI.Core/Shared/ProfiledCliCommand.cs` — base for commands that connect to Dataverse via a profile.
- `src/TALXIS.CLI.Core/Shared/OutputFormatter.cs` and `src/TALXIS.CLI.Shared/OutputWriter.cs` — write **command result data** (stdout). Use `OutputFormatter`. Do **not** call `OutputWriter` directly except inside renderer lambdas (analyzer TXC003 enforces this).
- `src/TALXIS.CLI.Core/Shared/TxcOutputJsonOptions.cs` — JSON options for stdout JSON (camelCase, kebab-case enums, indented, ignore nulls).
- `src/TALXIS.CLI.Logging/TxcLoggerFactory.cs` — `TXC_LOG_FORMAT=json` (or stdout redirected) routes ILogger to stderr-JSON.
- Attributes: `[CliReadOnly]`, `[CliIdempotent]`, `[CliDestructive]` (+ `IDestructiveCommand`), `[CliWorkflow(...)]`, `[CliLongRunning]`. Apply correctly per command.

### Existing data services and commands (touch points for the milestones)

- `src/TALXIS.CLI.Features.Data/DataPackageImportCliCommand.cs` — Phase A entry point; M4 will add Phase B post-processing here.
- `src/TALXIS.CLI.Features.Data/DataPackageExportCliCommand.cs`
- `src/TALXIS.CLI.Features.Data/DataPackageConvertCliCommand.cs` — naïve XLSX converter; replaced by `XlsxToCmtConverter` (M2) and key-aware rewriter (M5).
- `src/TALXIS.CLI.Features.Data/DataModelConvertCliCommand.cs` — solution → DBML/SQL/EDMX. Useful precedent for "schema in, artifact out".
- `src/TALXIS.CLI.Features.Data/TransformCliCommand.cs` — already exposes `/ComputePrimaryKey`; M5 promotes it into a shared `IRecordKeyService`.
- `src/TALXIS.CLI.Platform.Dataverse.Application/Services/DataverseDataPackageService.cs` — bridge to CMT subprocess.
- `src/TALXIS.CLI.Platform.Xrm/CmtImportRunner.cs` and `CmtExportRunner.cs` — the actual subprocess runners. Treat as Phase A primitives.
- `src/TALXIS.CLI.Platform.Dataverse.Data/` — `IDataverseQueryService`, `IDataverseRecordService`, `IDataverseBulkService`, `IDataverseEntityMetadataService`, `IDataverseRelationshipService`, `IDataverseFileService`. M4 adds Assignment / State / Process / Execution / Workflow services into the same area (`Application/Services/...`).
- `src/TALXIS.CLI.Platform.Dataverse.Application/Extensions/DataverseApplicationServiceCollectionExtensions.cs` — DI registrations.
- `src/TALXIS.CLI.Analyzers/MustNotCallOutputWriterAnalyzer.cs` — TXC003 rule referenced above.

### Existing docs to extend

- `docs/configuration-migration.md` — primary CMT doc; M1, M2, M5 will extend it.
- `docs/configuration-migration-sidecars.md` — NEW (M3 creates).
- `docs/data-migration-runtime.md` — NEW (M4 creates).

## 4. Reference repos (research artifacts)

- **Decompiled CMT (latest):** `/Users/tomasprokop/Desktop/Repos/jan.hajek-PPToolsChangeTracking/decompiled/Microsoft.CrmSdk.XrmTooling.ConfigurationMigration.Wpf/DataMigrationUtility/`
  Key files referenced during research (for sanity-checks):
  - `DataMigrationUtility/SchemaGenerator.cs` (62 KB, WPF-coupled — confirms why we must re-implement schema gen).
  - `DataMigrationUtility/ImportCrmDataHandler.cs` — match-by-GUID + `primarynamefield` fallback; no alt keys.
  - `DataMigrationUtility/ImportCrmEntityActions.cs` — owner Assign + plugin disable.
  - `DataMigrationUtility/ImportCommonMethods.cs` — `GetStatusAndState()`.
  - Search for `enabledDuplicateDetection` to see the hard-coded false.
- **Decompiled Dataverse 9.2 server / tools:** `/Users/tomasprokop/Desktop/Repos/D365CE-server-9.2.25063` (consult only when SDK signature is unclear).
- **Microsoft Learn** — primary spec for `SetStateRequest`, `AssignRequest`, `RetrieveProcessInstancesRequest`, `SetProcessRequest`, `ExecuteWorkflowRequest`, BPF entity model. Always link the Learn page in the implementation PR.

## 5. Repo-level conventions (quick reference)

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

## 6. Glossary

| Term | Meaning |
|---|---|
| CMT | Microsoft Configuration Migration Tool — exports/imports `data.xml` + `data_schema.xml`. |
| Phase A | Existing CMT import via `CmtImportRunner` subprocess. |
| Phase B | New in-process post-processor that runs sidecar operations (M4). |
| Sidecar | An XML file alongside the CMT package that carries non-CMT operations. |
| Manifest | `txc-package.xml`; the only file Phase B must read first; binds sidecars. |
| Alt key | Alternate key — one or more fields used to identify a record by natural value. txc resolves these via FetchXML; no physical Dataverse alternate key required. |
| Mechanism A | Convert-time deterministic GUID synthesis from alt-key tuple (MD5). Lets CMT match by GUID across re-runs. |
| Mechanism B | Apply-time alt-key resolution via FetchXML for sidecars and lookup tokens. |
| BPF | Business Process Flow — a workflow record (`Category=4`) and a per-record `bpf_<entity>` row carrying `activestageid`. |
| Custom API / Custom Action | Dataverse-side operations invoked via generic `OrganizationRequest.Execute`. |
| Probe | Optional FetchXML in `data_actions.xml` that gates whether a `<call>` or `<execute>` runs (for idempotency). |

## 7. Suggested starting order for a developer

1. Skim `00-overview.md`, then this file, then `01-assessment.md`.
2. Pick **M1** (`02-schema-generation.md`) — it unblocks M2 and is independently shippable.
3. While M1 is in flight, design review **M3** (`04-sidecar-formats.md`) — formats are the contract everything else depends on; lock them down early.
4. **M2** (`03-xlsx-roundtrip.md`) builds on M1.
5. **M5** (`06-idempotency-and-keys.md`) and **M4** (`05-state-machine-runtime.md`) both consume M3; M4 also benefits from M5's resolver.
6. Cross-cutting (`07-cli-command-surface.md`) closes the loop on docs / output contracts.

## 8. What is *not* yet decided (open items)

These are intentionally left for the implementation PRs. They do not change architecture.

- Should `generate-schema` accept multiple `--tables-file` inputs and merge? (Probably yes.)
- BPF version pinning — default to latest, allow `versionnumber` pin attribute? (Probably yes.)
- A `--force-actions` flag for M4 even when probes say no-op? (Probably yes.)
- Whether to keep the `--no-schema` shim on `convert` after one release. (Removal target TBD.)

## 9. Things explicitly out of scope

- Replacing the CMT engine.
- Live BPF *definition* editing (we only move records between defined stages).
- Hosting Package Deployer.
- Any web/UI surface.
- `data.xml → XLSX` reverse conversion (first cut).
- Auto-creating stub records on Mechanism B miss (future, behind a flag).
