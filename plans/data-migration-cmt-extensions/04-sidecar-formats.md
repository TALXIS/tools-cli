# M3 — Sidecar XML Formats and Package Manifest

> Goal: Define the source-controllable sidecar artifacts that carry non-CMT concerns (alternate keys, owners, state, BPF, custom actions) and a manifest that ties them to a CMT package.

## Principles

- Each sidecar is an XML file that mirrors CMT's style (lowercase elements, attribute-heavy, no namespaces required for human authoring).
- Sidecars **never** modify `data.xml` or `data_schema.xml`. Microsoft tools keep working.
- Each sidecar is independently optional. A package with only `data.xml` + `data_schema.xml` (no manifest) behaves exactly like today.
- Each operation references CMT records by `(entity, id)` — the same `id` used in `data.xml`. M5 adds resolution by alternate key.

## Files

### `txc-package.xml` — manifest

```xml
<txcPackage version="1">
  <cmt schema="data_schema.xml" data="data.xml" />
  <sidecars>
    <keys path="data_keys.xml" />
    <owners path="data_owners.xml" />
    <state path="data_state.xml" />
    <bpf path="data_bpf.xml" />
    <actions path="data_actions.xml" />
    <postimport path="data_postimport.xml" />
  </sidecars>
  <options>
    <plugins bypass="true" />            <!-- mirrors disableplugins; default off -->
    <continueOnError>false</continueOnError>
    <dryRun>false</dryRun>
  </options>
</txcPackage>
```

The manifest is the **only** entry point that the post-processor reads. If a package directory contains a `txc-package.xml`, its `sidecars/*` paths win. Missing sidecar files are not an error.

### `data_keys.xml` — alternate keys (M5)

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
- Resolution rule used by other sidecars: `<recordRef entity="account" key="byNumber" value="ACC-001" />`.
- Multi-field keys → `value` becomes `field1=v1;field2=v2`.
- These do NOT need to map to physical Dataverse alternate keys; txc resolves by FetchXML query.

### `data_owners.xml`

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

### `data_state.xml`

```xml
<state>
  <entity name="incident">
    <set id="22222222-..." state="resolved" status="problemsolved" />
  </entity>
</state>
```

- Issued via `SetStateRequest` (or `Update` of statecode/statuscode where `SetStateRequest` is rejected).
- Names may be the option-set label OR numeric value.

### `data_bpf.xml`

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

### `data_actions.xml`

```xml
<actions>
  <call name="new_promoteRecord">                          <!-- Custom API or Custom Action -->
    <bind type="EntityReference" name="Target"
          entity="account" id="44444444-..." />
    <param name="Mode" type="String" value="Strict" />
  </call>
  <execute workflow="Auto Activate New Account" id="44444444-..." />
</actions>
```

- `<call>` issues a generic `OrganizationRequest` with the named message — works for both *unbound* Custom APIs and *bound* Custom Actions.
- `<execute workflow="...">` issues `ExecuteWorkflowRequest` against a record.
- Parameter types follow Dataverse type names (`String`, `Int`, `Bool`, `EntityReference`, `Money`, `OptionSetValue`, `Decimal`, `DateTime`, `Guid`, `EntityCollection`).

### `data_postimport.xml` — execution order

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
- Each `<step>` is applied as a transaction batch by the post-processor (M4).

## Validation rules

A future `txc data package validate <path>` command will check:

- All `id`/`keyRef` references resolve to records present in `data.xml`.
- `<entity>`/`<field>` names appear in `data_schema.xml` (where applicable).
- Option-set values exist in target metadata (warning when not connected; hard error when `--profile` is supplied).
- `processName`/`stage` references in `data_bpf.xml` exist in target env (only when `--profile` given).
- Manifest `cmt/data` and `cmt/schema` paths exist.

## Source-control ergonomics

- All files are sorted deterministically (entities alphabetically; records by id; etc.) so diffs are minimal.
- Empty sidecars are emitted only as needed; absence is the default.
- Each sidecar carries a `<!-- generated-by: txc <version> -->` comment when produced by tooling, not when hand-written.

## Implementation outline

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
│   └── PostImportSidecar.cs / Reader
├── PackageLayout.cs                   # discovers files in a folder/zip
└── PackageValidator.cs                # cross-file consistency checks
```

No CLI commands added in this milestone — formats and parsers only. M4 wires them into import; a `validate` command lands later.

## Tests

- Round-trip XML serialization of each sidecar (write → read → equality).
- Cross-reference validator finds missing ids, unknown entities.
- Manifest with absent sidecars is treated as empty.

## Done when

- All sidecar parsers exist with full unit-test coverage.
- A reference example package is checked into `tests/Fixtures/migration-package/` and parses end-to-end.
- Documentation page added: `docs/configuration-migration-sidecars.md`.
