# Data Migration — Tool Selection Logic

<!-- Internal reasoning skill: contains ONLY volume-based routing and ordering constraints. -->
<!-- For tool descriptions, CMT pipeline details, and best practices, see the public data-migration skill. -->

## Volume-Based Tool Selection
```
How many records?
  ├─ 1 record        → environment_data_record_create or _update
  ├─ 2–100 records   → environment_data_bulk_upsert
  ├─ 100+ records    → CMT pipeline: data_package_export → data_package_import
  └─ Full env clone  → CMT pipeline with complete schema file
```

## Source-Based Tool Selection
```
Where does the data come from?
  ├─ User provides values inline    → environment_data_record_create / _update
  ├─ Another Dataverse environment  → data_package_export (source) → data_package_import (target)
  └─ User has structured data ready → data_package_import with prepared CMT package
```

## Query Tool Selection
```
User wants to query/verify data:
  ├─ Simple counts, filters         → environment_data_query_sql (SQL-like, quick)
  ├─ Complex filters, expand related → environment_data_query_odata
  └─ Verify after migration          → environment_data_query_sql (count + spot-check key fields)
```

## Ordering Constraints
→ ALWAYS export before importing when migrating between environments
→ ALWAYS test import on non-production before production
→ ALWAYS verify record counts after import (use `environment_data_query_sql`)
→ If stakeholder review is needed: export → review CMT package → import

## Anti-Patterns
- ❌ Using `environment_data_record_create` in a loop for 50+ records → use `environment_data_bulk_upsert`
- ❌ Importing to production without testing on dev/test first
- ❌ Skipping post-import verification → silent data issues
- ❌ Using CMT pipeline for a single record → unnecessary complexity
