# Milestone 2 — Staging Excel Format

## Goal

Generate a typed, validated Excel workbook that serves as the **contract** between upstream tools (human editors, scripts, Power Automate flows) and `txc`. This is the most critical milestone — the Excel format defines the data authoring experience and drives all downstream conversion.

This milestone has three sub-milestones:

- **M2a** — Template Generation (`txc data package generate-xlsx`)
- **M2b** — Reference Data Loading (`txc data package load-xlsx-refs`)
- **M2c** — Excel Library Integration (ClosedXML)

---

## Why

The existing `txc data package convert` is one-way (XLSX → XML), schema-unaware, and treats every cell as a string. Real users need:

- A pre-typed template they didn't have to build by hand.
- Dropdowns for option sets / status / boolean / (small) lookups.
- Validation that catches mistakes before import.
- Deterministic conversion that preserves data types CMT understands.

---

## M2a — Template Generation

### CLI Command

```
txc data package generate-xlsx
  --schema <path>                 # required
  --output <path>                 # required (.xlsx)
  --profile <name>                # optional; needed for --include-lookups and optionset metadata
  --entities <comma-list>         # optional; subset of schema entities to include as migrated
  --ref-entities <comma-list>     # optional; entities to include as reference-only
  --include-lookups               # default on; populates lookup sheets from environment if --profile given
  --include-optionsets            # default on
  --sample-rows <n>               # default 0; pre-fill blank GUIDs in column A for n rows
```

### Workbook Structure

The workbook uses a **unified entity sheet** model — there are no separate "lookup" sheets. Every entity (migrated or reference-only) gets its own sheet with the same layout. The only difference is the `include_in_cmt` flag.

#### Sheet Types

| Sheet Type         | Tab Naming Convention             | Purpose                                           |
|--------------------|-----------------------------------|---------------------------------------------------|
| Entity sheets      | Display name                      | One per entity (migrated + reference-only)         |
| Optionset sheets   | `OS: <Entity> - <Field>`          | "Label [value]" pairs for picklist validation      |
| M:N junction sheets| `M2M: <RelationshipName>`         | Two composite dropdown columns for many-to-many    |
| Custom API sheets  | `API: <DisplayName>`              | Per-API parameter columns, schema from env metadata|
| BPF sheet          | `BPF Advancement`                 | Entity, Record, Process, Stage columns             |
| Instructions sheet | `_instructions`                   | Visible workbook usage notes, edit rules, and conversion expectations |
| `_meta` sheet      | `_meta`                           | Schema fingerprint, txc version, field map         |

#### Sheet Naming Rules

- Excel tab names are limited to **31 characters**.
- Names exceeding 31 chars are truncated with `…` suffix.
- The full logical name is always available in the header metadata row (Row 1).
- The `_meta` sheet is **visible** and placed **last** (not hidden or veryHidden).

#### No Hidden Sheets

All sheets are visible. Full transparency — no hidden or veryHidden sheets. This is a deliberate departure from native Dataverse export (which uses veryHidden metadata sheets).

Technical helper columns can still be hidden or locked when they exist only to support formulas/dropdowns (for example the final composite `"Name [GUID]"` column). This is not hidden business metadata: the column purpose, formula, and named range are documented in `_instructions` and `_meta`.

#### `_instructions` Sheet

The generated workbook includes a visible `_instructions` sheet near the front of the workbook. It explains:

- Which rows/columns users should edit.
- What composite dropdown values mean (`"Label [value]"`, `"Name [GUID]"`).
- How row checksums are used for diff/tamper detection.
- What `include_in_cmt` means for migrated vs reference-only sheets.
- How deleted rows, added rows, and modified rows are interpreted by `convert`.
- Where to find detailed metadata in `_meta`.

### Per-Entity `include_in_cmt` Flag

Each entity sheet carries a metadata flag:

| Value   | Meaning                                                              |
|---------|----------------------------------------------------------------------|
| `true`  | Entity is included in CMT data.xml export — records will be migrated |
| `false` | Reference-only — records are loaded from target env for FK dropdowns |

Reference-only entities (e.g., SystemUser, Team, TransactionCurrency) are populated by `load-xlsx-refs` (M2b) and exist solely to provide valid dropdown values for lookup columns on migrated entities.

### Entity Sheet Layout

| Row   | Content                                                                    |
|-------|----------------------------------------------------------------------------|
| Row 1 | Entity metadata — merged cells, bold 14pt, display name + description      |
| Row 2 | Color legend — explains what each column color means                       |
| Row 3 | Display name headers — colored by field type, auto-filter enabled          |
| Row 4 | Logical names — gray italic, **visible** (NOT hidden)                      |
| Row 5+| Data rows                                                                  |

### Column Layout

| Column | Content                                                                            |
|--------|------------------------------------------------------------------------------------|
| A      | Primary key GUID — gray background, editable                                       |
| B      | Row checksum — SHA-256 hash of row data, base64-encoded. For tamper/delta detection |
| C..N   | Data fields — color-coded by field type                                            |
| Last   | Hidden composite column ("Name [GUID]") via formula, used as named range for FK dropdowns |

### Data Validation by Type

| Field Type                          | Validation Rule                                                                                          |
|-------------------------------------|----------------------------------------------------------------------------------------------------------|
| `string` / `memo`                   | `textLength ≤ max`                                                                                       |
| `int` / `bigint`                    | Whole number range                                                                                       |
| `decimal` / `money` / `float`       | Decimal range                                                                                            |
| `bool`                              | List: `Yes`, `No`                                                                                        |
| `datetime`                          | Date format validation                                                                                   |
| `picklist` / `state` / `status`     | Named-range dropdown → optionset sheet composite "Label [value]" column. Global optionsets get a single sheet; local optionsets get a per-entity-field sheet. |
| `entityreference` / `customer` / `owner` | Named-range dropdown → target entity sheet composite column. **NEVER use INDIRECT** (verified broken in spike). 32K item limit; **fail** if exceeded. |
| `multiselectpicklist`               | Free text (comma-separated labels)                                                                       |

### Styling

Based on the Python prototype ReviewEngine styling:

#### Color Scheme

| Element                    | Header Color                    | Cell Color          |
|----------------------------|---------------------------------|---------------------|
| Primary key / checksum     | Dark gray                       | Light gray          |
| Editable target fields     | Green (#548235)                 | White               |
| Optionset fields           | Purple tint                     | Purple tint (light) |
| Lookup / FK fields         | Blue tint                       | Blue tint (light)   |
| Required empty fields      | —                               | Yellow conditional  |
| Invalid cells              | —                               | Red conditional     |

#### Additional Styling

- **Legend row** (Row 2) with color key explaining all column colors.
- **Cell comments** on header cells (Row 3): logical name, field type, max length, lookup target, description.
- **Excel Table object** wrapping the data range — enables auto-filter + `TableStyleMedium2` alternating row stripes.
- **Frozen panes** at Row 5, Column C (data headers and key columns always visible).
- **Tab colors**: entity = green, reference-only = gray, optionset = purple, M:N = blue, API = orange, BPF = teal.
- **Column auto-fit** capped at 55 characters width.

### Sheet Protection

- Data columns (C..N) are **unlocked** — users can edit freely.
- Technical columns (A = primary key, B = checksum, last = composite) are **locked**.
- Reference-only entity sheets are **fully locked** (read-only).
- No password — protection is guidance, not security.

### BPF Resolution

BPF stages are referenced by **display name**, not by GUID. Stage GUIDs differ between environments (dev vs. prod), so GUID-based references would break on cross-environment migration.

At import time, the runtime resolves display names to GUIDs by querying the `processstages` table in the target environment. The `traversedpath` field is auto-computed from the ordered stage sequence.

### Custom API Sheets

Custom API parameter schema is loaded from the target environment metadata by introspecting `RequestParameter` definitions for each registered Custom API. Each API gets its own sheet with columns matching its parameters.

---

## M2b — Reference Data Loading

### Why a Separate Command?

Template generation (M2a) can run offline — it only needs the `data_schema.xml`. But reference data loading requires connectivity to the **target** environment to fetch actual records for reference-only entities.

Separating these concerns means:
1. Templates can be generated and distributed without environment access.
2. Reference data can be refreshed independently (e.g., after new users are added to target env).

### CLI Command

```
txc data package load-xlsx-refs --workbook migration.xlsx --profile target-dev [--entities SystemUser,Team]
```

### Behavior

1. Opens the workbook generated by M2a.
2. Identifies reference-only entity sheets (`include_in_cmt = false`).
3. Queries the target environment for all records of each reference entity.
4. Populates the entity sheets with fetched data.
5. Rebuilds composite columns ("Name [GUID]") and named ranges.
6. Refreshes optionset sheets with current values from the environment.
7. Validates that no entity exceeds **32,768 records** (Excel data validation item limit). Fails with a clear error if exceeded.

### Optional `--entities` Filter

By default, all reference-only entities are loaded. The `--entities` flag allows loading a subset (useful for large environments where only specific reference entities need refreshing).

---

## M2c — Excel Library Integration

### Package: ClosedXML

- **NuGet**: `ClosedXML`
- **License**: MIT (compatible with our repository license)
- **Why**: Wraps the existing `DocumentFormat.OpenXml` (OpenXml SDK) with a much friendlier API for cell formatting, data validation, named ranges, and table objects.

### Known Pitfalls (from spike)

1. **Never call `CreateDataValidation()` twice on the same cell range.** Doing so creates an orphaned `sqref=""` node in the XML that corrupts the workbook. Always check for existing validations before adding new ones.
2. **Use named ranges for cross-sheet validation, NOT `INDIRECT()`.** The `INDIRECT` function was verified broken for data validation dropdowns in the spike — Excel silently ignores `INDIRECT`-based validation lists in some scenarios. Named ranges work reliably.
3. **Deterministic file output.** Workbook must be saved with deterministic ordering of sheets, parts, and shared strings so two runs against the same schema produce identical files.

### Implementation

New namespace: `TALXIS.CLI.Features.Data/Xlsx/`

#### Core Types

| Type                     | Responsibility                                                       |
|--------------------------|----------------------------------------------------------------------|
| `StagingExcelGenerator`  | Generates the template workbook from schema + metadata               |
| `StagingExcelReader`     | Reads a populated workbook back into typed records                   |
| `ReferenceDataLoader`    | Fetches reference data from target env and populates sheets          |
| `ExcelStyleConstants`    | Central definition of all colors, fonts, and style constants         |
| `ColumnDefinition`       | Describes a single column: logical name, type, validation, styling   |
| `DataValidationBuilder`  | Creates Excel data validation rules per field type                   |
| `SchemaReader`           | Parses `data_schema.xml` into a typed model                         |
| `OptionSetCache`         | Keyed by entity+attribute; pulled via `IDataverseEntityMetadataService` when `--profile` is set |

New CLI command: `DataPackageGenerateXlsxCliCommand`.
Refactor: `DataPackageConvertCliCommand` to delegate to `XlsxToCmtConverter`.
DI: register the converter and template generator alongside other Data services.

---

## Tests

| Test                              | Description                                                             |
|-----------------------------------|-------------------------------------------------------------------------|
| Template layout                   | Generate from fixture schema → re-open → assert rows, columns, styles   |
| Data validation                   | Assert correct validation type per field type                           |
| Named ranges                      | Assert named ranges exist and point to correct composite columns        |
| Optionset sheets                  | Assert "Label [value]" format, correct values                           |
| Reference data loading            | Load from mock env → assert sheets populated, composites rebuilt        |
| 32K limit enforcement             | Entity with >32K records → assert failure with clear message            |
| Sheet protection                  | Assert data columns unlocked, technical columns locked                  |
| Round-trip with data              | Generate → fill data rows → read back → assert values match            |
| Determinism                       | Same inputs → identical workbook on repeated runs                       |

## Done When

- `txc data package generate-xlsx` produces a typed Excel workbook with all sheet types, styling, and validation
- `txc data package load-xlsx-refs` populates reference sheets from a live environment
- The generated workbook is accepted by `txc data package convert` (M4)
- All tests pass
