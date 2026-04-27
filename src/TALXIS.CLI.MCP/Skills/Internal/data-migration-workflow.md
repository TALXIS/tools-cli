# Data Migration Workflow

## CMT Data Package Pipeline

### Step 1: Export Data
```
Tool: data_package_export
```
Exports data from a Dataverse environment using a schema file that defines which tables and columns to include. Produces a data package with XML data files.

### Step 2: Transform Data (if needed)
```
Tool: data_package_convert
```
Converts between formats:
- XLSX → CMT data package (for importing spreadsheet data)
- CMT data package → XLSX (for human review/editing)

Use this when source data comes from Excel files or when stakeholders need to review data before import.

### Step 3: Import Data
```
Tool: data_package_import
```
Imports the data package into the target environment. Handles record creation and updates based on the schema mapping.

## Individual Record Operations

For small-scale or targeted data changes:

| Operation | Tool | When to Use |
|---|---|---|
| Create single record | `environment_data_record_create` | One-off record creation |
| Update single record | `environment_data_record_update` | Targeted field updates |
| Bulk upsert | `environment_data_bulk_upsert` | Creates or updates by key, best for batches |

## Query & Verification

After migration, verify data integrity:

| Tool | Use Case |
|---|---|
| `environment_data_query_sql` | SQL-like queries for quick verification |
| `environment_data_query_odata` | OData queries for complex filtering |

## Decision Tree: Which Approach?

```
How much data?
  ├─→ Single record → environment_data_record_create/update
  ├─→ Small batch (10-100) → environment_data_bulk_upsert
  ├─→ Large dataset (100+) → CMT pipeline (export → convert → import)
  └─→ Full environment migration → CMT pipeline with complete schema
```

## Best Practices
- Always export from source first to understand the data shape
- Use `data_package_convert` to let stakeholders review in Excel
- Test import on a non-production environment before production
- Verify record counts and key fields after import
- For recurring migrations, save and version the schema file
