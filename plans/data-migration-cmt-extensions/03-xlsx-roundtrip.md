# M2 — XLSX Round-Trip

> Goal: Business users get a typed, validated Excel template per `data_schema.xml`, fill it in, and we convert it deterministically back to a CMT `data.xml`.

## Why

The existing `txc data package convert` is one-way (XLSX → XML), schema-unaware, and treats every cell as a string. Real users need:

- A pre-typed template they didn't have to build by hand.
- Dropdowns for option sets / status / boolean / (small) lookups.
- Validation that catches mistakes before import.
- Deterministic conversion that preserves data types CMT understands.

## Architecture

Two new commands; existing `convert` is hardened:

```
generate-xlsx     schema -> .xlsx (template)
convert           .xlsx + schema -> data.xml          (existing, schema-aware now)
validate-xlsx     .xlsx + schema -> report (no env)   (optional, deferred)
```

A schema is **mandatory** input for both directions to make conversions type-safe and reversible.

## XLSX template format

One workbook per package. Layout:

| Sheet | Purpose |
|---|---|
| `_README` | Hand-off instructions for the business user (auto-generated). |
| `_meta` (hidden) | txc metadata: schema fingerprint, generation timestamp, txc version. |
| `<entity>` (one per `<entity>` in schema) | Header row = field display names; second row (frozen, hidden) = field logical names; data rows below. |
| `_lookups_<entity>` (one per referenced parent) | Two columns: `id` + `displayname`. Used for dropdown source ranges and to seed alternate-key references. |
| `_optionset_<field>` | Two columns: `value` + `label`. Backing data for option-set dropdowns. |

Rules:

- Column order matches `<fields>` order in schema.
- Cell formatting follows CMT type:
  - `string`/`memo` → text.
  - `int`/`bigint` → integer.
  - `decimal`/`money`/`float` → number with currency/decimal hint.
  - `bool` → list validation `Yes`/`No` (mapped to `true`/`false` on convert).
  - `datetime` → ISO date/datetime; date-only when metadata says `DateOnly`.
  - `picklist`/`state`/`status` → dropdown sourced from `_optionset_<field>`.
  - `multiselectpicklist` → free-text with hint "comma-separated values from _optionset_<field>".
  - `entityreference`/`customer`/`owner` → free-text *primary-key value* by default; if `_lookups_<target>` exists, dropdown sourced from `id` column.
  - `guid` → text, regex-validated.
  - `uniqueidentifier` → primary-key column, frozen first column, regex-validated.
- All physical column widths set with sensible defaults; auto-filter on header row; freeze panes on row 3.
- Workbook saved with deterministic ordering of sheets, parts, and shared strings (so two runs against the same schema produce identical files).

## Inputs

```
txc data package generate-xlsx
  --schema <path>                 # required
  --output <path>                 # required (.xlsx)
  --include-lookups               # default on; populates _lookups_* from environment if --profile given
  --include-optionsets            # default on
  --sample-rows <n>               # default 0; pre-fill blank GUIDs in column A for n rows
  --profile <name>                # optional; needed only for --include-lookups
```

```
txc data package convert
  --input <path.xlsx>
  --schema <path>                  # NEW required arg (was missing before)
  --output <path.xml>              # data.xml
  --on-error <fail|skip-row|warn>  # default: fail
```

(The current schema-less mode of `convert` is **deprecated** but kept for one release behind `--no-schema` for backward compatibility.)

## Type coercion table

| Schema type | Cell raw | Emitted XML `value` | Notes |
|---|---|---|---|
| `string` | `"abc"` | `abc` | Trim trailing whitespace optional. |
| `int` | `42` / `42.0` | `42` | Reject `4.5`. |
| `decimal`/`money`/`float` | `1.23` | `1.23` | Use invariant culture. |
| `bool` | `Yes`/`No`/`true`/`false`/`1`/`0` | `true`/`false` | Case-insensitive. |
| `datetime` | Excel date / ISO string | ISO 8601 UTC | Honor schema `dateMode`. |
| `picklist` | label or numeric value | numeric value | Resolve via `_optionset_<field>`. |
| `multiselectpicklist` | `"label1, label2"` | `value1;value2` | Sentinel `-1` is forbidden — error. |
| `entityreference` | GUID or alt-key string | GUID | Alt-key reference is rewritten via M5 (sidecar `data_keys.xml`); this milestone only emits raw value. |
| `state` / `status` | label or value | numeric value | Same as picklist. |
| `guid` | text | text (validated) | Regex `^[0-9a-f-]{36}$`. |

## Reverse direction (CMT data.xml → XLSX)

Out of scope for first cut; revisit if customers ask. Most teams treat XML as the authored artifact in source control once a baseline is captured.

## Implementation outline

New types in `TALXIS.CLI.Features.Data`:

```
Xlsx/
├── XlsxTemplateGenerator.cs        # schema -> XLSX
├── XlsxToCmtConverter.cs           # XLSX + schema -> data.xml (replaces ad-hoc code in DataPackageConvertCliCommand)
├── XlsxTypeCoercion.cs             # central table
├── SchemaReader.cs                 # parses data_schema.xml into a typed model
└── OptionSetCache.cs               # keyed by entity+attribute; pulled via IDataverseEntityMetadataService when --profile is set
```

New CLI: `DataPackageGenerateXlsxCliCommand`.
Refactor: `DataPackageConvertCliCommand` to delegate to `XlsxToCmtConverter`.

DI: register the converter and template generator alongside other Data services.

## Tests

- Generate template from a fixture schema → re-open → assert sheet/column layout.
- Round-trip: generate template, fill via OpenXml in test, convert back, parse `data.xml`, assert types and values.
- Determinism: two generations produce identical bytes.
- Failure modes: bad bool, bad int, unknown picklist label → clear error per `--on-error`.

## Done when

- `txc data package generate-xlsx --schema schema.xml -o tpl.xlsx` produces a typed template.
- Hand-filled template converts via `txc data package convert` to a `data.xml` accepted by `txc data package import`.
- Documentation updated.
