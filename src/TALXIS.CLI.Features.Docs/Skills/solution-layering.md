# Solution Layering & Segmentation

## Managed vs Unmanaged

| Aspect | Unmanaged | Managed |
|---|---|---|
| Use case | Development | Production deployment |
| Editable in target | Yes | No (locked) |
| Uninstall | Components remain | Clean removal |
| Source control | Source of truth | Generated artifact |

Develop unmanaged, deploy managed.

## The Layer Stack

When several solutions modify one component, layers decide the result: the active (unmanaged) layer wins, then managed solutions from most to least recently imported, then the System base. Higher layers override lower ones.

## Solution Segmentation (default layout)

Discover the repository's solutions first (`dotnet sln list`); otherwise separate concerns:

- **Solutions.DataModel**: tables, columns, relationships, option sets
- **Solutions.Logic**: plugins, workflows, business rules
- **Solutions.UI**: forms, views, model-driven apps
- **Solutions.Security**: security roles, field-level security

This gives independent deployment cycles, smaller packages, clear ownership and fewer merge conflicts.

## Inspection Commands

| Command | Purpose |
|---|---|
| `txc environment solution list [--managed true\|false]` | Solutions in the environment |
| `txc environment solution get <solution-name>` | Version, publisher, etc. |
| `txc environment solution component list <solution-name>` | Components in a solution |
| `txc environment component layer list --id <guid> --type <type>` (or `--entity <logicalName> [--attribute <name>]`) | Layer stack of a component |
| `txc environment component layer get --id <guid> --type <type>` (same selectors) | One layer entry |

"Which solution owns this component?": `layer list` for it (`--entity` for a table, plus `--attribute` for a column); the topmost managed layer is the effective owner.

## Publisher Rules

Check the publisher and prefix the repository or environment already uses (`txc environment publisher list`); otherwise:
- One publisher per organization; all components share its prefix
- Prefix: recommended max 5 lowercase alphanumeric characters; the command accepts 2-8 alphanumeric, starting with a letter, not starting with `mscrm`

```
txc environment publisher create <unique-name> --display-name "<Display Name>" --prefix <prefix> --option-value-prefix <10000-99999>
```

## What NOT to Do

- ❌ Don't deploy unmanaged solutions to production
- ❌ Don't put everything in one solution when the repository has no convention: segment by concern
- ❌ Don't uninstall without `txc environment solution uninstall-check <solution-name>`: it may cascade-delete data
- ❌ Don't ignore layer conflicts: the topmost layer wins, and the unmanaged active layer overrides everything

See also: [deployment-workflow](deployment-workflow.md), [troubleshooting](troubleshooting.md)
