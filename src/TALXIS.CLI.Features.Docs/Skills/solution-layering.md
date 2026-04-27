# Solution Layering & Segmentation

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

## Inspection Tools

| Tool | Purpose |
|---|---|
| `environment_solution_list` | List all solutions in the environment |
| `environment_solution_show` | Details of a specific solution (version, publisher, etc.) |
| `environment_solution_component_list` | All components in a solution |
| `environment_component_layer_list` | Layer stack for a specific component |
| `environment_component_layer_show` | Details of a specific layer entry |

## Common Workflows

### "Which solution owns this component?"
1. `environment_component_layer_list` with the component ID
2. Review the layer stack — the topmost managed layer is the effective owner
3. `environment_component_layer_show` for details on specific layers

### "What's in this solution?"
1. `environment_solution_show` for solution metadata
2. `environment_solution_component_list` for the full component inventory

### Publisher Rules
- One publisher per organization for consistency
- Prefix: max 8 characters (e.g., `contoso`, `udpp`)
- All components created under this publisher share the prefix

See also: [deployment-workflow](deployment-workflow.md), [troubleshooting](troubleshooting.md)
