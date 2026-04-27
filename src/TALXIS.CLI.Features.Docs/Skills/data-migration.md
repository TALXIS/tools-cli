# Data Migration & Seed Data

## CMT Data Package Pipeline

The Configuration Migration Tool (CMT) pipeline is the standard approach for moving data between Dataverse environments.

### Export
```
Tool: data_package_export
```
Exports data from a source environment using a schema file that defines which tables and columns to include. Produces XML data files packaged together.

### Transform (Optional)
```
Tool: data_package_convert
```
Converts between formats:
- **XLSX → CMT package**: Import data from Excel spreadsheets
- **CMT package → XLSX**: Export for human review or stakeholder sign-off

### Import
```
Tool: data_package_import
```
Imports the data package into the target environment. Handles record creation, updates, and relationship mapping.

## When to Use What

| Scenario | Approach | Tools |
|---|---|---|
| Single record creation | Record-level | `environment_data_record_create` |
| Single record update | Record-level | `environment_data_record_update` |
| Batch of 10–100 records | Bulk upsert | `environment_data_bulk_upsert` |
| Large dataset (100+) | CMT pipeline | `data_package_export` → `data_package_convert` → `data_package_import` |
| Full environment clone | CMT pipeline | Export all tables with complete schema |
| Seed/reference data | CMT pipeline | Version the schema file in source control |

## Bulk Operations

### `environment_data_bulk_upsert`
Creates or updates records based on a key field. Best for:
- Syncing reference data across environments
- Loading seed data during environment provisioning
- Batch updates where you have a natural key

## Query & Verification

After any data operation, verify the results:

| Tool | Best For |
|---|---|
| `environment_data_query_sql` | Quick counts and simple filters (SQL-like syntax) |
| `environment_data_query_odata` | Complex filtering, expand related records |

### Verification Checklist
- Record counts match expected totals
- Key fields are populated correctly
- Relationships (lookups) resolve to correct records
- Option set values map correctly

## Common Scenarios

### Seeding Reference Data from Excel
```
1. data_package_convert { sourceFile: "reference-data.xlsx", targetFormat: "CMT" }
2. data_package_import { packagePath: "<output from step 1>" }
3. environment_data_query_sql { query: "SELECT COUNT(*) FROM prefix_referencetable" }
```

### Cloning Data Between Environments
```
1. data_package_export { schemaFile: "migration-schema.xml" }     — on source env profile
2. Switch to target environment profile
3. data_package_import { packagePath: "<output from step 1>" }
4. Verify: environment_data_query_sql { query: "SELECT COUNT(*) FROM prefix_tablename" }
```

## Best Practices
- Always test imports on a non-production environment first
- Version schema files in source control for repeatable migrations
- Use `data_package_convert` to let stakeholders review data in Excel before import
- For recurring migrations, automate the export → convert → import pipeline

## What NOT to Do
- ❌ Don't use `environment_data_record_create` in a loop for bulk data — use `environment_data_bulk_upsert` or the CMT pipeline
- ❌ Don't import directly to production without testing on dev/test first
- ❌ Don't skip post-import verification — silent data issues are hard to catch later

See also: [environment-management](environment-management.md), [troubleshooting](troubleshooting.md)
