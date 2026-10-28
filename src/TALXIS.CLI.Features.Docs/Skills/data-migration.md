# Data Migration & Seed Data

## CMT Data Package Pipeline

The Configuration Migration Tool (CMT) pipeline is the standard way to move data between environments.

### Export
```
txc data package export --schema <schema-file.xml> --output <package-dir> [--zip] [--overwrite] [--export-files]
```
The schema file defines the tables and columns. Output is a folder, or a `.zip` with `--zip`; `--overwrite` is needed to replace an existing output; `--export-files` also downloads file and image columns.

### Import
```
txc data package import <package-dir-or-zip> [--batch-mode] [--batch-size <n>]
```
Handles creates, updates and relationship mapping. `--batch-mode` is much faster for large imports but skips many-to-many relationship records, so omit it (or run a second pass without it) when the package contains them. `--batch-size` (only with `--batch-mode`) sets records per batch request (default 600); lower it if imports hit timeouts or server errors.

## When to Use What

| Scenario | Commands |
|---|---|
| Single record create | `txc environment data record create --entity <logicalName> --data '<json>' --apply` |
| Single record update | `txc environment data record update <record-guid> --entity <logicalName> --data '<json>' --apply` |
| Up to a few hundred records | `txc environment data bulk upsert --entity <logicalName> --file <records.json>` |
| Larger datasets, environment clone, seed/reference data | CMT: `txc data package export` → `txc data package import` (version the schema file in source control) |

`--data` takes inline JSON (single quotes suit bash; PowerShell and cmd.exe differ); `--file <path>` avoids quoting.

Record-level commands (`create`, `update`, `delete`, ...) and the entity, column, relationship and option-set mutating commands need exactly one of `--apply` (run now) or `--stage` (add to a changeset; run later with `txc environment changeset apply --strategy batch|transaction|bulk`). `environment data bulk` and the package commands do not.

## Bulk Operations

```
txc environment data bulk upsert --entity <logicalName> --file <records.json>
```
`--file` is a JSON array of records (or pass it inline with `--data`). Each record is created or updated by the primary or alternate key it carries; there is no separate key option. `bulk create` and `bulk update` take the same arguments. Good for syncing reference data and seeding environments.

## Verify

After any data operation, check record counts, key fields, lookups resolving to the right records, and option-set values:
- `txc environment data query sql "<select>"`: counts, simple filters and joins
- `txc environment data query odata <entity-set> --filter "<expr>"`: filter and select columns on one table

## Cloning Data Between Environments
```
1. txc data package export --schema migration-schema.xml --output <package-dir> --profile <source-profile>
2. txc data package import <package-dir> --profile <target-profile>
3. txc environment data query sql "SELECT COUNT(*) FROM <prefix_tablename>" --profile <target-profile>
```

## Best Practices
- Test imports on a non-production environment first
- Import related tables in dependency order: tables without foreign keys first, then dependents; `bulk upsert` makes re-runs idempotent
- Before importing records with lookups, query the target table for a name→GUID map to fill the lookup fields

## What NOT to Do
- ❌ Don't loop `record create` for bulk data: use `bulk upsert` or the CMT pipeline
- ❌ Don't import to production without testing on dev/test
- ❌ Don't skip post-import verification

See also: [environment-management](environment-management.md), [troubleshooting](troubleshooting.md)
