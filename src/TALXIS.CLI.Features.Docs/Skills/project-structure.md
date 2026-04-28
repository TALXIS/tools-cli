# Workspace & Project Organization

## Overview

A Power Platform monorepo follows a structured layout where solution projects, plugin projects, and package deployer projects coexist. Use `workspace_explain` to inspect and understand your current repository structure.

## Repository Structure Convention

```
├── src/                          # Source code directory
│   ├── Solutions.DataModel/      # Dataverse schema and data model (.cdsproj)
│   ├── Solutions.Logic/          # Business logic and plugins (.cdsproj)
│   ├── Solutions.UI/             # User interface components (.cdsproj)
│   ├── Solutions.Security/       # Security roles and permissions (.cdsproj)
│   ├── Plugins.{Domain}/        # Plugin projects (e.g., Plugins.Warehouse)
│   └── Packages.Main/           # Package Deployer project
├── pipelines/                   # CI/CD pipeline definitions
│   ├── build.yml
│   ├── deploy.yml
│   └── test.yml
└── tests/                       # Test projects
```

## Project Types

### Solution Projects (`.cdsproj`)
These contain Dataverse solution metadata and components as XML files:
- **Solutions.DataModel** — Tables, columns, relationships, option sets
- **Solutions.Logic** — Workflows, business rules, plugin step registrations
- **Solutions.UI** — Forms, views, model-driven apps, sitemaps
- **Solutions.Security** — Security roles, field-level security profiles

Each `.cdsproj` maps to one Dataverse solution. The `Declarations` folder within a solution project holds the component XML files.

### Plugin Projects
- Pattern: `Plugins.{DomainArea}` (e.g., `Plugins.Warehouse`, `Plugins.Sales`)
- Standard .NET class libraries containing plugin classes
- Plugin classes follow `{Action}{Entity}Plugin.cs` naming (e.g., `ValidateWarehouseTransactionPlugin.cs`)
- Referenced by solution projects that register the plugin steps

### Package Deployer Projects
- `Packages.Main` — orchestrates deployment of multiple solutions in order
- Defines import sequence and any pre/post-deployment data operations
- Used for full environment provisioning

## Key Tools

| Tool | Purpose |
|---|---|
| `workspace_explain` | Understand the current repo structure, solutions, and components |
| `workspace_component_type_list` | List available component types for scaffolding |
| `workspace_component_create` | Scaffold new components into a solution project |

## Naming Conventions

- **Entity logical names**: lowercase with publisher prefix (e.g., `prefix_warehouseitem`)
- **Display names**: Proper case (e.g., `Warehouse Item`)
- **Publisher prefix**: Recommended convention is lowercase alphanumeric and 5 characters or fewer for consistency
- **Branch naming**: `{userPrefix}/{feature-description}` (trunk-based development)

## Getting Started

1. Run `workspace_explain` to understand what's already in the repo
2. Identify which solution project your changes belong to
3. Use `workspace_component_create` to scaffold
4. Build locally to validate before deploying

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
