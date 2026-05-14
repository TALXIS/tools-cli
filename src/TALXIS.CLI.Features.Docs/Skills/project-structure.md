# Workspace & Project Organization

## Overview

A Power Platform monorepo follows a structured layout where solution projects, plugin projects, and package deployer projects coexist. Use `workspace_explain` to inspect and understand your current repository structure.

## Repository Structure Convention

```
├── src/                          # Source code directory
│   ├── Solutions.DataModel/      # Dataverse schema and data model (.cdsproj)
│   ├── Solutions.Logic/          # Business logic and plugins (.cdsproj)
│   ├── Solutions.UI/             # User interface components (.cdsproj)
│   ├── Solutions.UI.Scripts/     # TypeScript web resource library (.csproj)
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

All projects use `<ProjectType>` in their `.csproj` to declare their type. The Build SDK routes each type to its specific build logic automatically.

### Solution Projects (`ProjectType=Solution`)
These contain Dataverse solution metadata and components as XML files:
- **Solutions.DataModel** — Tables, columns, relationships, option sets
- **Solutions.Logic** — Workflows, business rules, plugin step registrations
- **Solutions.UI** — Forms, views, model-driven apps, sitemaps
- **Solutions.Security** — Security roles, field-level security profiles

Each solution project maps to one Dataverse solution. Solution projects are the **hub** — they reference other project types via `dotnet add reference`, and the Build SDK auto-handles each reference type during `dotnet build`.

### Plugin Projects (`ProjectType=Plugin`)
- Scaffold with `pp-plugin` → `pp-plugin-assembly` → `pp-plugin-assembly-step`
- .NET class libraries containing plugin classes that extend `PluginBase`
- Referenced by solution projects — Build SDK auto-generates plugin assembly data.xml

### Script Library Projects (`ProjectType=ScriptLibrary`)
- Scaffold with `pp-script-library` (provides `PublisherPrefix` and `LibraryName`)
- TypeScript + Rollup project producing a single UMD JavaScript web resource
- The UMD global (`prefix_name`) is what Dataverse forms and ribbon buttons reference as function namespace (e.g., `prefix_name.Main.onLoad`)
- Referenced by solution projects — Build SDK auto-generates web resource data.xml
- `dotnet build` handles everything: npm install → Rollup bundle → web resource registration
- No need to run `pp-webresource` manually or call npm directly

### Workflow Activity Projects (`ProjectType=WorkflowActivity`)
- Scaffold with `pp-workflow-activity`
- .NET class libraries containing custom workflow steps that extend `WorkflowActivityBase`
- Referenced by solution projects — Build SDK auto-generates assembly data.xml and copies DLL

### Code App Projects (`ProjectType=CodeApp`)
- Scaffold with `pp-app-code`
- Node.js/TypeScript projects for Power Apps code-first apps
- Referenced by solution projects — Build SDK runs npm build and generates CanvasApp metadata

### PCF Control Projects
- Scaffold with `pp-pcf`
- Uses Microsoft's PAC infrastructure directly
- Referenced by solution projects — handled by standard PAC pipeline

### Package Deployer Projects (`ProjectType=PdPackage`)
- Scaffold with `pp-package`
- Orchestrates deployment of multiple solutions in order
- References solution projects — collects their output ZIPs for packaging

## Project References (`dotnet add reference`)

Solution projects act as the hub. When you add a project reference, the Build SDK **auto-detects the referenced project's type** via the `GetProjectType` MSBuild protocol and handles it accordingly:

```bash
# From a solution project directory:
dotnet add reference ../Plugins.Warehouse/Plugins.Warehouse.csproj      # Plugin → auto-generates assembly data.xml
dotnet add reference ../Solutions.UI.Scripts/Solutions.UI.Scripts.csproj  # ScriptLibrary → auto-generates web resource
dotnet add reference ../WorkflowActivities/WorkflowActivities.csproj     # WorkflowActivity → auto-generates assembly data.xml
dotnet add reference ../Solutions.DataModel/Solutions.DataModel.csproj    # Solution → dependency chain
```

All auto-generation happens during `dotnet build` — no manual registration steps needed.

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
