# Milestone 4b — CMT Package → Staging Excel Conversion (Reverse)

## Goal

Enable **reverse conversion** from an existing CMT data package back into a staging Excel workbook. This completes the round-trip: export from Dataverse → review in Excel → edit → convert back → re-import.

## CLI Command

```
txc data package export-xlsx --package ./package/ -o migration.xlsx [--profile target-dev]
```

### Arguments

| Argument      | Description                                                              |
|---------------|--------------------------------------------------------------------------|
| `--package`   | Path to the CMT package directory (containing data.xml + data_schema.xml)|
| `-o`          | Output path for the generated Excel workbook                             |
| `--profile`   | Optional but recommended — needed for display name resolution            |

## Why This Matters

Without reverse conversion, the workflow is one-directional: Excel → CMT. With it, developers can:

1. **Export** existing data from Dataverse via CMT.
2. **Review** the data in a familiar Excel format with full styling and validation.
3. **Edit** records (fix data, add rows, modify values).
4. **Convert** back to CMT format for re-import.
5. **Re-import** with all post-import operations intact.

This is especially valuable for iterative migration scenarios where data is exported, reviewed, corrected, and re-imported multiple times.

## How It Works

### Reuses Existing Components

- **`StagingExcelGenerator`** (from M2a) — generates the workbook structure, styling, validation, and named ranges.
- **`ReferenceDataLoader`** (from M2b) — if a profile is provided, populates reference entity sheets from the target environment.

### New Component

- **`data.xml` parser** — reads the CMT data file and feeds records into the `StagingExcelGenerator` as pre-populated data rows.

### Value Resolution (Reverse Direction)

The converter resolves raw CMT values back into the human-friendly composite format used in the staging Excel:

| CMT Value (data.xml)        | Excel Value                          | Resolution Method                    |
|-----------------------------|--------------------------------------|--------------------------------------|
| Optionset integer (`1`)     | `"Active [1]"`                       | Schema metadata → label lookup       |
| Lookup GUID (`a1b2c3d4-...`)| `"Contoso Ltd [a1b2c3d4-...]"`       | Target env query (needs profile)     |
| Boolean (`true`)            | `"Yes"`                              | Static mapping                       |
| DateTime (ISO 8601)         | Excel date cell                      | Direct conversion                    |

**Note**: Lookup GUID → display name resolution requires environment connectivity (profile). Without a profile, lookup columns show the raw GUID only: `"[a1b2c3d4-...]"` (no display name).

### `include_in_cmt` Flag

All entity sheets in the exported workbook have `include_in_cmt = true` because every entity in the source package was part of the CMT export. There are no reference-only entities in this direction (they can be added later with `load-xlsx-refs` if needed).

## Implementation

New type: `CmtToXlsxConverter` in `TALXIS.CLI.Features.Data/Xlsx/`

The converter:

1. Parses `data_schema.xml` to understand the entity structure.
2. Parses `data.xml` to extract all records.
3. Passes the schema to `StagingExcelGenerator` to create the workbook structure.
4. Fills entity sheets with resolved record data.
5. Writes the source row catalog `(entity, id, checksum)` into `_meta` so the forward `convert` command can classify unchanged, modified, added, and deleted rows.
6. If a profile is available, uses `ReferenceDataLoader` to populate reference data and resolve lookup display names.
7. Writes the workbook to the output path.

## Tests

| Test                              | Description                                                           |
|-----------------------------------|-----------------------------------------------------------------------|
| Full round-trip                   | export → export-xlsx → convert → compare data.xml (should be equal)   |
| Optionset reverse resolution      | Integer in data.xml → "Label [value]" in Excel                       |
| Lookup reverse resolution         | GUID in data.xml → "Name [GUID]" in Excel (with mock env)            |
| Lookup without profile            | GUID in data.xml → "[GUID]" in Excel (no display name)               |
| Bool reverse                      | true/false → "Yes"/"No"                                              |
| DateTime reverse                  | ISO 8601 → Excel date cell                                           |
| Row catalog                       | `_meta` contains entity/id/checksum entries for diff classification  |
| All entities marked migrated      | Assert all sheets have include_in_cmt = true                         |
| Styling preserved                 | Assert generated workbook has correct styling, validation, and layout |
| Determinism                       | Same package → identical workbook on repeated runs                   |

## Done When

- `txc data package export-xlsx` produces a valid staging Excel workbook
- The generated workbook is accepted by `txc data package convert` (M4)
- Round-trip: `export` → `export-xlsx` → `convert` produces equivalent `data.xml`
- All tests pass
