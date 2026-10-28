# Data Migration & Seed Data

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## CMT Data Package Pipeline

The Configuration Migration Tool (CMT) pipeline is the standard approach for moving data between Dataverse environments.

### Export
```
txc data package export --schema <schema-file.xml> --output <package-dir> [--zip] [--overwrite] [--export-files]
```
Exports data from a source environment using a schema file that defines which tables and columns to include. Produces XML data files packaged together (a folder, or a `.zip` with `--zip`; `--overwrite` is needed to replace an existing output; `--export-files` also downloads file and image columns).

### Import
```
txc data package import <package-dir-or-zip> [--batch-mode] [--batch-size <n>]
```
Imports the data package into the target environment. Handles record creation, updates, and relationship mapping. `--batch-mode` is much faster for large imports but skips many-to-many relationship records, so omit it (or run a second pass without it) when the package contains them.

## When to Use What

| Scenario | Approach | Commands |
|---|---|---|
| Single record creation | Record-level | `txc environment data record create --entity <logicalName> --data '<json>' --apply` |
| Single record update | Record-level | `txc environment data record update <record-guid> --entity <logicalName> --data '<json>' --apply` |
| A small batch of records (rule of thumb: up to a few hundred) | Bulk upsert | `txc environment data bulk upsert --entity <logicalName> --file <records.json>` |
| Larger datasets (rule of thumb: beyond a few hundred records) | CMT pipeline | `txc data package export` → `txc data package import` |
| Full environment clone | CMT pipeline | Export all tables with complete schema |
| Seed/reference data | CMT pipeline | Version the schema file in source control |

`--data` takes inline JSON; the single quotes above suit bash, and quoting differs in PowerShell and cmd.exe. `--file <path>` avoids quoting altogether.

The record-level commands (`txc environment data record create`, `update`, `delete`, ...) and the entity, column, relationship and option-set mutating commands need exactly one of `--apply` (run now) or `--stage` (add to a changeset; run it later with `txc environment changeset apply --strategy batch|transaction|bulk`). The `environment data bulk` commands and the package commands do not.

## Bulk Operations

### `txc environment data bulk upsert`
```
txc environment data bulk upsert --entity <logicalName> --file <records.json>
```
`--file` is a JSON array of records (or pass the array inline with `--data`). Creates or updates each record by its primary or alternate key, which the record itself carries; there is no separate key option. `environment data bulk create` and `bulk update` take the same arguments. Best for:
- Syncing reference data across environments
- Loading seed data during environment provisioning
- Batch updates where you have a natural key

## Query & Verification

After any data operation, verify the results:

| Command | Best For |
|---|---|
| `txc environment data query sql "<select>"` | Quick counts, simple filters and joins (SQL-like syntax) |
| `txc environment data query odata <entity-set> --filter "<expr>"` | Filtering and selecting columns on one table |

### Verification Checklist
- Record counts match expected totals
- Key fields are populated correctly
- Relationships (lookups) resolve to correct records
- Option set values map correctly

## Common Scenarios

### Cloning Data Between Environments
```
1. txc data package export --schema migration-schema.xml --output <package-dir> --profile <source-profile>
2. (Target environment: pass --profile <target-profile> on the next commands, or make it active with txc config profile select <target-profile>)
3. txc data package import <package-dir> --profile <target-profile>
4. Verify: txc environment data query sql "SELECT COUNT(*) FROM <prefix_tablename>" --profile <target-profile>
```

## Best Practices
- Always test imports on a non-production environment first
- Version schema files in source control for repeatable migrations
- For recurring migrations, automate the export → import pipeline

## What NOT to Do
- ❌ Don't use `txc environment data record create` in a loop for bulk data — use `txc environment data bulk upsert` or the CMT pipeline
- ❌ Don't import directly to production without testing on dev/test first
- ❌ Don't skip post-import verification — silent data issues are hard to catch later

## Bulk Import Best Practices

**Batch Size:** for `txc data package import --batch-mode`, `--batch-size` sets the records per batch request (default 600; lower is safer, higher is faster). If imports hit timeouts or server errors, lower it and run again.

**FK-Ordered Import:** When importing multiple related tables, import in dependency order:
1. Tables with no foreign keys first (reference data)
2. Then tables that depend on them (transactional data)
3. Use `txc environment data bulk upsert` for idempotent re-runs

**Lookup Resolution:** Before importing records with lookups, query the target table to build a name→GUID map. Use this to populate lookup fields in the import data.

See also: [environment-management](environment-management.md), [troubleshooting](troubleshooting.md)
