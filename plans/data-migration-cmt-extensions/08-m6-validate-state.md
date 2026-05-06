# Milestone 6 — Post-Import State Validation

## Goal

Provide a CLI command that validates the live Dataverse environment matches the expected state defined by the data package. This is the final verification step after import + post-import operations.

## CLI Command

```
txc data package validate-state --package ./package/ --profile target-dev [--output report.json] [--on-mismatch warn|fail]
```

### Arguments

| Argument         | Description                                                            |
|------------------|------------------------------------------------------------------------|
| `--package`      | Path to the CMT package directory                                      |
| `--profile`      | Profile for connecting to the target environment                       |
| `--output`       | Optional path for the JSON validation report (default: stdout)         |
| `--on-mismatch`  | `warn` (default) logs mismatches; `fail` exits with non-zero code      |

## Validation Checks

The validator reads the package (data.xml + sidecars) and compares against the live environment:

| Check                  | What It Validates                                                       |
|------------------------|-------------------------------------------------------------------------|
| Record count           | Expected number of records per entity matches actual count              |
| Record existence       | Every record in data.xml exists in the target environment               |
| Field values           | Field values match between package and live records                     |
| FK integrity           | Lookup fields point to valid, existing records                          |
| Optionset values       | Picklist values match expected values from the package                  |
| State/status           | statecode + statuscode match `data_state.xml` declarations             |
| Owner                  | Record owner matches `data_owners.xml` declarations                    |
| BPF stage              | BPF active stage matches `data_bpf.xml` declarations                   |
| M:N associations       | Many-to-many relationships exist as declared in the package             |

## JSON Validation Report

```json
{
  "timestamp": "2025-01-15T11:00:00Z",
  "package": "./package/",
  "environment": "https://org.crm.dynamics.com",
  "summary": {
    "totalEntities": 5,
    "totalRecords": 1500,
    "passedRecords": 1485,
    "failedRecords": 15,
    "mismatches": 22
  },
  "entities": [
    {
      "name": "account",
      "expectedCount": 500,
      "actualCount": 500,
      "missingRecords": 0,
      "fieldMismatches": [
        {
          "recordId": "a1b2c3d4-...",
          "field": "ownerid",
          "expected": "John Smith",
          "actual": "Jane Doe",
          "type": "owner"
        }
      ]
    }
  ]
}
```

The report provides:

- **Per-entity stats**: expected vs. actual record counts, missing records, mismatch counts.
- **Per-record field mismatches**: exact field, expected value, actual value, and mismatch type.
- **Summary**: aggregate pass/fail counts for quick assessment.

This command validates the package against target Dataverse state. It is not a complete migration QA framework: source-to-staging reconciliation, sampling approvals, tolerance rules, business sign-off reports, and coverage analysis remain upstream/future concerns. The report shape should leave room to add those later without changing the core package-state checks.

## Future Validation Extensions

Future milestones should add:

- Source → staging → target reconciliation using source lineage metadata.
- Sampling/sign-off reports for business users.
- Tolerance rules for fields where exact equality is not appropriate.
- Coverage analysis that identifies source rows not represented in the package and target records not covered by the migration.
- Binary/file integrity checks for annotations and file columns.

## Implementation

### Data Retrieval

Uses `IDataverseQueryService` (FetchXML or OData) to read target environment data. Queries are batched per entity to minimize round-trips.

### New Service

| Type                     | Interface                  | Responsibility                              |
|--------------------------|----------------------------|---------------------------------------------|
| `PackageStateValidator`  | `IPackageStateValidator`   | Orchestrates all validation checks          |

The validator:

1. Loads the package (data.xml + all sidecars).
2. For each entity, queries the target environment for all expected records.
3. Compares field-by-field, owner, state, BPF stage.
4. For M:N relationships, queries the intersect entity.
5. Collects all mismatches into the report.

### Integration with Existing Services

Reuses services from M5:

- `IDataverseAssignmentService` — for owner comparison
- `IDataverseStateService` — for state/status comparison
- `IDataverseProcessService` — for BPF stage comparison

## Tests

| Test                              | Description                                                        |
|-----------------------------------|--------------------------------------------------------------------|
| All matching                      | Mock env matches package → report shows zero mismatches            |
| Missing records                   | Record in package not in env → reported as missing                 |
| Field value mismatch              | Different field value → reported with expected vs. actual          |
| Owner mismatch                    | Different owner → reported                                        |
| State mismatch                    | Different statecode → reported                                    |
| BPF stage mismatch                | Different BPF stage → reported                                    |
| M:N association missing           | Association in package not in env → reported                      |
| Record count mismatch             | Different count → reported in entity stats                        |
| Report JSON format                | Assert report JSON matches expected schema                        |
| `--on-mismatch fail`              | Mismatches present → non-zero exit code                           |
| Empty package                     | No records → valid report with zero counts                        |
| Future-compatible report          | Unknown future sections can be ignored by older readers           |

## Done When

- `txc data package validate-state` correctly identifies all mismatches between package and live environment
- Report JSON contains accurate per-entity and per-record detail
- `--on-mismatch fail` returns non-zero exit code when mismatches exist
- All tests pass
