# Troubleshooting Common Issues

## Deployment Failures

**Start here:** `txc environment deployment get --latest` (or `--async-operation-id <guid>`, the id `txc environment solution import` printed). Never query the `asyncoperation` table via `txc environment data query sql`: it returns raw status codes and unparsed error XML.

Commands that act on one component take `--id <guid> --type <type>` or `--entity <logicalName> [--attribute <name>]` (the type is detected with `--entity`).

```
Import failed
  ├─→ Component conflict
  │    └─→ txc environment component layer list --id <component-guid> --type <type>
  │         └─→ Find the solution owning the conflicting component; update it first or remove the conflict
  ├─→ Missing dependency
  │    └─→ txc environment component dependency required --id <component-guid> --type <type>
  │         └─→ Import the solution containing the required component first
  ├─→ Solution already exists (version conflict)
  │    └─→ Increment the solution version and retry
  └─→ Timeout or generic failure
       └─→ Check environment health, retry (`--wait` only when the caller has no timeout; otherwise poll `deployment get`, see deployment-workflow)
```

## Component Conflicts

When a component behaves unexpectedly or changes don't take effect:
1. `txc environment component layer list --entity <logicalName>` (or `--id <guid> --type <type>`): all solutions customizing it
2. `txc environment component layer get --entity <logicalName>` (same selectors): what each layer contributes
3. The topmost layer wins: update the topmost managed solution, remove the unmanaged active layer, or reorder imports
4. Change not visible: `txc environment solution publish`

## Authentication Issues

401/403 or connection errors: [environment-management](environment-management.md), Troubleshooting Auth.

## Dependency Problems

- Can't delete a component: `txc environment component dependency delete-check --entity <logicalName> [--attribute <name>]` (or `--id <guid> --type <type>`) shows what depends on it; remove or update those first.
- Missing required dependencies: `txc environment component dependency required --entity <logicalName> [--attribute <name>]` shows what the component requires; make sure those exist in the target.

## Wrong Environment

Data or schema not as expected: [environment-management](environment-management.md), Switching Environments (`txc config profile get` shows the active profile and its URL).

## Common Dataverse Error Codes

| Hex Code | Meaning | Recovery |
|---|---|---|
| `0x80040216` | Entity relationship already exists | Skip or use force-overwrite on import |
| `0x80048d19` | Duplicate alternate key value | Check key columns for unique constraint violations |
| `0x80040237` | Record does not exist | Verify the GUID; it may have been deleted |
| `0x8004431a` | Privilege denied | Check the user's security roles |
| `0x80060891` | Async operation failed | Check the async operation for details |
| `0x80040220` | Attribute already exists | Skip or import with overwrite flag |

Metadata lock ("another operation is running against this entity"): wait 10-30 seconds and retry; avoid parallel schema changes on one entity.

See also: [deployment-workflow](deployment-workflow.md), [solution-layering](solution-layering.md)
