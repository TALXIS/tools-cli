# Milestone 3 — Sidecar XML Formats and Package Manifest

> Goal: Define the source-controllable sidecar artifacts that carry non-CMT concerns (alternate keys, owners, state, BPF, custom actions, impersonation) and a manifest that ties them to a CMT package.

## Principles

- Each sidecar is an XML file that mirrors CMT's style (lowercase elements, attribute-heavy, no namespaces required for human authoring).
- Sidecars **never** modify `data.xml` or `data_schema.xml`. Microsoft tools keep working.
- Each sidecar is independently optional. A package with only `data.xml` + `data_schema.xml` (no manifest) behaves exactly like today.
- Each operation references CMT records by `(entity, id)` — the same `id` used in `data.xml`. M5 adds resolution by alternate key.

## Sidecar Files

### `txc-package.xml` — Manifest

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
  <options>
    <plugins bypass="true" />                          <!-- mirrors disableplugins; default off -->
    <bypassBusinessLogic>CustomAsync</bypassBusinessLogic> <!-- CustomAsync | CustomSync | Both | none (default) -->
    <bypassPluginStepIds>                              <!-- bypass specific plugin step GUIDs (optional) -->
      <step id="a1b2c3d4-..." />
    </bypassPluginStepIds>
    <suppressPowerAutomateFlows>true</suppressPowerAutomateFlows>  <!-- SuppressCallbackRegistrationExpanderJob -->
    <suppressDuplicateDetection>true</suppressDuplicateDetection>  <!-- SuppressDuplicateDetection -->
    <autoDisassociate>false</autoDisassociate>          <!-- AutoDisassociate header for lookup reassignment -->
    <coalesceNonEmptyValues>false</coalesceNonEmptyValues> <!-- Only update non-empty fields; empty = don't touch -->
    <continueOnError>false</continueOnError>
    <dryRun>false</dryRun>
  </options>
  <metadata>
    <migrationBatch>svs-wave-01</migrationBatch>
    <sourceSystem>legacy-svs</sourceSystem>
  </metadata>
</txcPackage>
```

The manifest is the **only** entry point that the post-processor reads. If a package directory contains a `txc-package.xml`, its `sidecars/*` paths win. Missing sidecar files are not an error.

### `data_keys.xml` — Alternate Keys (used by M5)

```xml
<keys>
  <entity name="account">
    <key name="byNumber">
      <field name="accountnumber" />
    </key>
    <key name="byNameAndOwner">
      <field name="name" />
      <field name="owninguser" />
    </key>
  </entity>
</keys>
```

- Each `<entity>` declares one or more named alternate keys.
- A key is a list of CMT-recognizable fields (matching `<field>` elements in the schema).
- If the same natural keys can repeat across companies, partitions, or source systems, include those partition fields in the key declaration (for example `source_system`, `company_code`, or `migration_partition`). Deterministic GUIDs are only safe when the full tuple is globally unique for the target table.
- Resolution rule used by other sidecars: `<recordRef entity="account" key="byNumber" value="ACC-001" />`.
- Multi-field keys → `value` becomes `field1=v1;field2=v2`.
- These do NOT need to map to physical Dataverse alternate keys; txc resolves by FetchXML query.

### `data_owners.xml` — Owner Assignments

```xml
<owners>
  <entity name="account">
    <assign id="11111111-..." owner="systemuser:domain\\alice" />
    <assign keyRef="byNumber" keyValue="ACC-001" owner="team:Sales NA" />
  </entity>
</owners>
```

- `owner` format: `<systemuser|team>:<resolver>` where resolver is `domain\\name`, email, or `id:<guid>`.
- Issued via `AssignRequest`. Overrides any `ownerid` set by CMT.

### `data_state.xml` — State/Status Overrides

```xml
<state>
  <entity name="incident">
    <set id="22222222-..." state="resolved" status="problemsolved" />
  </entity>
</state>
```

- Issued via `SetStateRequest` (or `Update` of statecode/statuscode where `SetStateRequest` is rejected).
- Names may be the option-set label OR numeric value.

### `data_bpf.xml` — BPF Stage Moves

```xml
<bpf>
  <entity name="account">
    <move id="33333333-..." processName="Lead to Opportunity" stage="Qualify" />
  </entity>
</bpf>
```

- Resolves `processName` to a `workflow` record (`Category=4`).
- Resolves `stage` by name within that BPF.
- Sets `bpf_<entity>id` and `activestageid` on the target record (post-import).
- If the BPF instance does not exist for the record yet, txc creates it via `RetrieveProcessInstancesRequest` + `Create` of the `<bpfentity>` row.
- Uses **process display name + stage display name** (not GUIDs) because stage GUIDs differ between environments.
- At import time, the runtime resolves display names to GUIDs by querying the `processstages` table in the target environment. The `traversedpath` field is auto-computed from the ordered stage sequence.

### `data_actions.xml` — Custom API Calls, Workflows, and Query-Then-Update

Supports three patterns:

#### `<call>` — Custom API / Action Invocation

```xml
<actions>
  <call name="new_promoteRecord">                          <!-- Custom API or Custom Action -->
    <bind type="EntityReference" name="Target"
          entity="account" id="44444444-..." />
    <param name="Mode" type="String" value="Strict" />
  </call>
</actions>
```

- `<call>` issues a generic `OrganizationRequest` with the named message — works for both *unbound* Custom APIs and *bound* Custom Actions.

#### `<execute>` — Workflow Trigger

```xml
<actions>
  <execute workflow="Auto Activate New Account" id="44444444-..." />
</actions>
```

- `<execute workflow="...">` issues `ExecuteWorkflowRequest` against a record.

#### `<patch>` — Query-Then-Update Pattern (NEW)

```xml
<actions>
  <patch entity="account">
    <query>
      <!-- FetchXML to find target records -->
      <fetch>
        <entity name="account">
          <attribute name="accountid" />
          <filter>
            <condition attribute="industrycode" operator="eq" value="1" />
          </filter>
        </entity>
      </fetch>
    </query>
    <updates>
      <field name="creditlimit" value="50000" />
    </updates>
  </patch>
</actions>
```

#### Parameter Type Mapping

Generic mapping table (already used internally by Dataverse SDK):

| `type` attribute | C# / SDK type |
|---|---|
| `String` | `string` |
| `Int` | `int` |
| `Bool` | `bool` |
| `Decimal` | `decimal` |
| `Money` | `Money` |
| `DateTime` | `DateTime` |
| `Guid` | `Guid` |
| `OptionSetValue` | `OptionSetValue(int)` |
| `EntityReference` | `EntityReference(entity, id|keyRef)` |
| `EntityCollection` | nested `<entityCollection>` with `<entity>` children |
| `Entity` | nested `<entity>` with `<attribute>` children |

Unknown types → hard error with helpful list of supported types.

### `data_callerid.xml` — Per-Record Impersonation (NEW)

Declares per-record `MSCRMCallerID` impersonation. Allows specific records to be created/updated as a different user (e.g., preserving the original author of a note).

```xml
<callerids>
  <entity name="annotation">
    <impersonate id="55555555-..." caller="systemuser:domain\\jane" />
    <impersonate keyRef="byEmail" keyValue="jane@contoso.com" caller="systemuser:jane@contoso.com" />
  </entity>
</callerids>
```

- `caller` format follows the same `<systemuser|team>:<resolver>` pattern as `data_owners.xml`.

### `data_postimport.xml` — Execution Order

```xml
<postimport>
  <step kind="owners" />
  <step kind="bpf" />
  <step kind="state" />
  <step kind="actions">
    <only name="new_promoteRecord" />
  </step>
  <step kind="actions" />        <!-- everything else -->
</postimport>
```

- Optional. If absent, default order is `owners → state → bpf → actions`.
- `<only>` filters specific items; everything not enumerated runs in the next unfiltered step of that kind, or is dropped if no unfiltered step matches.
- Each `<step>` is applied as a transaction batch by the post-processor (M5).

## Sidecar Generation from Staging Excel

The `convert` command (M4) automatically generates sidecars from the staging Excel:

| Excel Source                 | Generated Sidecar        |
|------------------------------|--------------------------|
| `ownerid` column on entities | `data_owners.xml`        |
| `statecode` column           | `data_state.xml`         |
| BPF Advancement sheet        | `data_bpf.xml`           |
| API sheets (`API: ...`)      | `data_actions.xml`       |
| Caller ID data               | `data_callerid.xml`      |

Sidecars that have no corresponding data in the Excel are simply not generated (optional by design).

## Validation Rules

A future `txc data package validate <path>` command will check:

- All `id`/`keyRef` references resolve to records present in `data.xml`.
- `<entity>`/`<field>` names appear in `data_schema.xml` (where applicable).
- Option-set values exist in target metadata (warning when not connected; hard error when `--profile` is supplied).
- `processName`/`stage` references in `data_bpf.xml` exist in target env (only when `--profile` given).
- Target schema compatibility before import: required fields, field types, valid-for-create/update/read where relevant, lookup targets, option/status values, BPF processes/stages, Custom API/action names, and workflow names.
- Target security readiness before import where it can be checked cheaply: owner/caller users exist and are enabled, teams exist, and the import principal has privileges needed for bypass headers and impersonation.
- Manifest `cmt/data` and `cmt/schema` paths exist.
- `data_postimport.xml` must reference only sidecar kinds listed in `txc-package.xml`.
- No duplicate record IDs within a single sidecar entity block.
- Owner/caller names in `data_owners.xml` and `data_callerid.xml` must be non-empty strings.
- State/status codes in `data_state.xml` must be valid integers.

Optional source lineage metadata is allowed at package level (`<metadata>`) and can also be carried as normal data fields when the target model has fields such as `source_system`, `source_key`, or `migration_batch`. Phase 1 records and reports this metadata but does not provide a central lineage database.

## Source-Control Ergonomics

- All files are sorted deterministically (entities alphabetically; records by id; etc.) so diffs are minimal.
- Empty sidecars are emitted only as needed; absence is the default.
- Each sidecar carries a `<!-- generated-by: txc <version> -->` comment when produced by tooling, not when hand-written.

## Implementation

New project module: `TALXIS.CLI.Features.Data/Package/`:

```
Package/
├── Manifest/
│   ├── PackageManifest.cs             # POCO of txc-package.xml
│   └── PackageManifestReader.cs
├── Sidecars/
│   ├── KeysSidecar.cs / KeysSidecarReader.cs
│   ├── OwnersSidecar.cs / Reader
│   ├── StateSidecar.cs / Reader
│   ├── BpfSidecar.cs / Reader
│   ├── ActionsSidecar.cs / Reader
│   ├── CallerIdSidecar.cs / Reader
│   └── PostImportSidecar.cs / Reader
├── PackageLayout.cs                   # discovers files in a folder/zip
└── PackageValidator.cs                # cross-file consistency checks
```

No CLI commands added in this milestone — formats and parsers only. M5 wires them into import; a `validate` command lands later.

Each file type follows the same pattern: a POCO class for the data model + a static `Read(Stream)` / `Write(Stream)` pair for XML serialization.

## Tests

| Test                          | Description                                                       |
|-------------------------------|-------------------------------------------------------------------|
| Round-trip XML                | Write sidecar → read back → assert equal (each sidecar type)     |
| Cross-reference validation    | Record ID in sidecar not in data.xml → assert validation error    |
| Absent sidecars               | Package with no sidecars → manifest treated as empty, no errors   |
| Manifest consistency          | Manifest lists sidecar not on disk → assert validation error      |
| Post-import ordering          | Assert steps execute in declared order                            |
| Post-import `<only>` filter   | Assert specific items filtered correctly                          |
| BPF display name format       | Assert process + stage names (not GUIDs)                          |
| Actions idempotency probe     | Assert `<when>` FetchXML parsed correctly                         |
| Patch query parsing           | Assert `<patch>` FetchXML + updates parsed correctly              |
| CallerID format               | Assert caller resolver format parsed correctly                    |
| Parameter type mapping        | All supported types round-trip correctly                          |

## Done When

- All sidecar parsers exist with full unit-test coverage
- A reference example package is checked into `tests/Fixtures/migration-package/` and parses end-to-end
- `PackageValidator` catches all cross-file inconsistencies
- Documentation page added: `docs/configuration-migration-sidecars.md`
- All tests pass
