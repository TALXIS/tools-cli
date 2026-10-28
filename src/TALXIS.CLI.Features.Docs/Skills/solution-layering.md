# Solution Layering & Segmentation

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). If the TALXIS MCP server is connected instead, every command is also a tool named by joining its words with `_` (`txc workspace component create` is the tool `workspace_component_create`). The tool list is generated from the CLI commands, so each tool takes the arguments and options of its command, and the CLI help is the reference on either route.

## How Solutions Work in Dataverse

Solutions are containers for customizations. They package components (tables, forms, views, plugins, etc.) for transport between environments.

### Managed vs Unmanaged

| Aspect | Unmanaged | Managed |
|---|---|---|
| Use case | Development | Production deployment |
| Editable in target | Yes | No (locked) |
| Uninstallable | Components remain | Clean removal |
| Source control | Yes — this is your source of truth | Generated artifact |
| Versioning | N/A | Semantic versioning recommended |

**Rule of thumb:** Develop in unmanaged, deploy as managed.

## The Layer Stack

Dataverse applies customizations in layers. When multiple solutions modify the same component, the layer stack determines the final result:

```
┌─────────────────────┐  ← Top: Active customizations (unmanaged)
├─────────────────────┤
│  Solution C (managed) │  ← Most recently imported managed solution
├─────────────────────┤
│  Solution B (managed) │
├─────────────────────┤
│  Solution A (managed) │
├─────────────────────┤
│  System (base)       │  ← Bottom: Out-of-box Dataverse
└─────────────────────┘
```

Higher layers override lower layers. The active (unmanaged) layer always wins.

## Solution Segmentation Strategy

Separate concerns into dedicated solutions:

- **Solutions.DataModel** — Tables, columns, relationships, option sets
- **Solutions.Logic** — Plugins, workflows, business rules
- **Solutions.UI** — Forms, views, model-driven apps
- **Solutions.Security** — Security roles, field-level security

### Benefits
- Independent deployment cycles per concern
- Smaller packages = faster imports
- Clear ownership for team collaboration
- Reduced merge conflicts

## Inspection Commands

| Command | Purpose |
|---|---|
| `txc environment solution list [--managed true\|false]` | List all solutions in the environment |
| `txc environment solution get <solution-name>` | Details of a specific solution (version, publisher, etc.) |
| `txc environment solution component list <solution-name>` | All components in a solution |
| `txc environment component layer list --id <guid> --type <type>` (or `--entity <logicalName> [--attribute <name>]`) | Layer stack for a specific component |
| `txc environment component layer get --id <guid> --type <type>` (same selectors) | Details of a specific layer entry |

## Common Workflows

### "Which solution owns this component?"
1. `txc environment component layer list --id <component-guid> --type <type>` (or `--entity <logicalName>` for a table, add `--attribute <name>` for a column)
2. Review the layer stack — the topmost managed layer is the effective owner
3. `txc environment component layer get` with the same selectors for details on specific layers

### "What's in this solution?"
1. `txc environment solution get <solution-name>` for solution metadata
2. `txc environment solution component list <solution-name>` for the full component inventory

### Publisher Rules
Defaults, in order of precedence: what the user asks for, then the publisher and prefix the target repository or environment already uses (`txc environment publisher list`), then these:
- One publisher per organization for consistency
- Prefix: recommended convention is max 5 characters, lowercase alphanumeric only (e.g., `udpp`); the command itself accepts 2–8 alphanumeric characters, starting with a letter and not starting with `mscrm`
- All components created under this publisher share the prefix

```
txc environment publisher create <unique-name> --display-name "<Display Name>" --prefix <prefix> --option-value-prefix <10000-99999>
```

## What NOT to Do

- ❌ Don't deploy unmanaged solutions to production — always use managed for proper lifecycle management
- ❌ Don't put all components in a single solution — segment by concern for independent deployment cycles
- ❌ Don't uninstall solutions without running `txc environment solution uninstall-check <solution-name>` — may cascade-delete data
- ❌ Don't ignore layer conflicts — the topmost layer always wins, and unmanaged active layers override everything

See also: [deployment-workflow](deployment-workflow.md), [troubleshooting](troubleshooting.md)
