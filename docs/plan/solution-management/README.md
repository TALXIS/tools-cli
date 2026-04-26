# Solution Management Commands — Implementation Plan

## Vision

Make `txc` the best CLI for Dataverse ALM — more consistent, productive, and developer-friendly than first-party tools. Enable developers to inspect solutions, understand layering, diagnose dependencies, manage composition, and (in the future) sync local workspaces with Dataverse environments at near-instant speed.

## Design Principles

- **Developer-first** — easy to understand, productive, better than pac CLI
- **Consistent** — follows existing `txc` patterns: `[CliArgument]` for primary target, `[CliOption]` for modifiers
- **SDK-first** — all operations through `IOrganizationServiceAsync2` (ServiceClient); `ExecuteWebRequest` for Web API-only virtual entities
- **Uniform parameter naming** — solution name always as first positional arg for solution-scoped commands; component-id as first positional arg for component-scoped commands; `--type` for component type everywhere

## Command Surface

```
txc environment (alias: env)
│
├── solution (alias: sln)                    # Solution resource
│   ├── list                                 # ✅ EXISTS
│   ├── show <name>                          # Phase 1 — details + component type counts
│   ├── create <name>                        # Phase 2 — create unmanaged solution
│   ├── delete <name>                        # Phase 2 — delete unmanaged solution (container only)
│   ├── import <solution-zip>                # ✅ EXISTS
│   ├── uninstall <name>                     # ✅ EXISTS (enhance: managed-only + dep pre-check)
│   ├── uninstall-check <name>               # Phase 1 — can I safely uninstall?
│   ├── publish                              # Phase 2 — publish customizations
│   │
│   │ # Components within a solution
│   ├── component list <solution>            # Phase 1 — list components in this solution
│   ├── component count <solution>           # Phase 1 — component type counts
│   ├── component add <solution>             # Phase 3 — add existing component to solution
│   └── component remove <solution>          # Phase 3 — remove component from solution
│
└── component (alias: comp)                  # Component resource (solution-independent)
    ├── export <component-id>                # Phase 1 — export component via temp solution
    ├── layer
    │   ├── list <component-id>              # Phase 1 — show solution layer stack
    │   ├── show <component-id>              # Phase 1 — active layer definition (JSON)
    │   └── remove-customization <component-id>  # Phase 3 — remove unmanaged layer
    └── dependency (alias: dep)
        ├── list <component-id>              # Phase 1 — what depends on this
        ├── required <component-id>          # Phase 1 — what this depends on
        └── delete-check <component-id>      # Phase 1 — can I delete this?
```

### Resource Hierarchy Rationale

- **`env solution`** — solution CRUD + managing components *within* a solution. `sln component list/count/add/remove` take a solution name because they operate on the solution-component relationship.
- **`env component`** — component-level operations independent of any solution. Layers and dependencies exist on the component itself regardless of which solution it belongs to.
- **`sln uninstall-check`** stays under solution — it checks a *solution's* uninstall feasibility across all its components.

### Comparison with Local Workspace (feeds into Phase 4 sync)

To compare server state with local unpacked solution files, the CLI provides multiple levels:

**Single component:**
- **`comp export <component-id>`** → export one component via temp solution wrapper → unpacked files directly comparable with local workspace
- **`comp layer show <component-id>`** → active layer definition JSON (lighter, no temp solution)

**Full solution:**
- **`sln component list <solution>`** → inventory of all components (type + objectId + name)
- Iterate `comp export` or `comp layer show` for each component in the inventory
- Future: `sln export` for bulk solution export + unpack

The diff algorithm (detailed in [04-sync.md](./04-sync.md)):
- Hash each local unpacked component file (SHA-256 of canonical content)
- Hash each server component definition (from export or layer show)
- Diff the hash sets → identify Created, Modified, Deleted components
- For modified: generate minimal patch solution ZIP containing only changed components

## Milestones

| File | Phase | Description |
|------|-------|-------------|
| [00-infrastructure.md](./00-infrastructure.md) | 0 | Shared services, type resolver, schema constants |
| [01-inspection.md](./01-inspection.md) | 1 | Read-only commands: show, component list/count, layers, dependencies |
| [02-crud.md](./02-crud.md) | 2 | Solution create, delete, publish |
| [03-mutations.md](./03-mutations.md) | 3 | Component add/remove, layer remove-customization, uninstall enhancement |
| [04-sync.md](./04-sync.md) | 4 (future) | Client-side diff, workspace sync, patch generation |

## Research Artifacts

| File | Description |
|------|-------------|
| [research/api-test-results.md](./research/api-test-results.md) | Live API tests against org2928f636 |
| [research/har-analysis.md](./research/har-analysis.md) | HAR capture analysis from make.powerapps.com (338 calls) |
| [research/server-source-analysis.md](./research/server-source-analysis.md) | Decompiled D365CE-server-9.2.25063 analysis |
| [research/scf-components.md](./research/scf-components.md) | SCF (Solution Component Framework) research |
| [research/solution-packager-analysis.md](./research/solution-packager-analysis.md) | SolutionPackagerLib API, PAC CLI integration, licensing |
| [research/blog-post-ideas.md](./research/blog-post-ideas.md) | Blog post opportunities from discoveries |
| [research/contributing-audit-report.md](./research/contributing-audit-report.md) | Audit of plan files against CONTRIBUTING.md rules |

## Documentation Updates Per PR

Each PR that adds or modifies commands should include:
- Update CONTRIBUTING.md aliases table if new aliases are added
- Update README.md with example snippets for new commands

## Existing Command Patterns (for consistency)

- Leaf commands extend `ProfiledCliCommand` (provides `--profile`, `--verbose`, `--allow-production`)
- Safety: `[CliReadOnly]`, `[CliIdempotent]`, or `[CliDestructive]`
- Primary target → `[CliArgument(Name = "name")]`
- Modifiers → `[CliOption(Name = "--xxx")]`
- Service interface in `Core.Contracts.Dataverse`, implementation in `Platform.Dataverse.Application/Services/`
- SDK logic in `Platform.Dataverse.Application/Sdk/` taking `IOrganizationServiceAsync2`
- Connection: `DataverseCommandBridge.ConnectAsync()` → `conn.Client`
