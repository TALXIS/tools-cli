# Workspace & Project Organization

## Overview

A Power Platform monorepo follows a structured layout where solution projects, plugin projects, and package deployer projects coexist. To see what a repository already contains, read its solution file (`.slnx`, or `.sln` in older repos) and each project's `ProjectType`. `dotnet sln list` lists the projects of the solution file; searching the `.csproj` files for `<ProjectType>` shows each one's type.

## Repository Structure Convention

This is our default layout. Existing project names, folders and the solution file are respected, never renamed. By default every project lives under `src/` as an SDK-style `.csproj`, and all of them are listed in one solution file at the repository root (`.slnx`; older repos use `.sln`). Default project names, by role (examples, not a fixed list): `Solutions.*` (for example `Solutions.DataModel`, `Solutions.Logic`, `Solutions.UI`, `Solutions.Security`), `Plugins.{Domain}`, `Controls.{Domain}`, `Scripts.UI`, `Packages.Main`, and test projects as `src/Tests.*`. CI/CD workflows live in `.github/workflows/` by default. The names are a convention, not a requirement: what the build cares about is each project's `ProjectType` (below) and its references.

## Project Types

All projects use `<ProjectType>` in their `.csproj` to declare their type. The Build SDK routes each type to its specific build logic automatically.

### Solution Projects (`ProjectType=Solution`)
These contain Dataverse solution metadata and components as XML files (default names shown):
- **Solutions.DataModel** — Tables, columns, relationships, option sets
- **Solutions.Logic** — Workflows, business rules, plugin step registrations
- **Solutions.UI** — Forms, views, model-driven apps, sitemaps
- **Solutions.Security** — Security roles, field-level security profiles

Each solution project maps to one Dataverse solution. Solution projects are the **hub** — they reference other project types via `dotnet add reference`, and the Build SDK auto-handles each reference type during `dotnet build`.

### Plugin Projects (`ProjectType=Plugin`)
- Scaffold with `pp-plugin`, reference it from the solution project that holds the registrations (`dotnet add <solution-project> reference <plugin-project>`) and build that solution project (`dotnet build <solution-project>`), not just the plugin. Current Build SDKs register the assembly and generate `[PluginStep]` steps in that build; the exceptions (older SDK, `GeneratePluginAssembly=false`, repositories that register steps another way) are in [plugin-development](plugin-development.md), which should be read before running `pp-plugin-assembly`.
- .NET class libraries containing plugin classes that extend `PluginBase`
- Referenced by solution projects — the Build SDK generates the plugin assembly data.xml when that solution project is built (current SDKs)

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

### PCF Control Projects (`ProjectType=Pcf`)
- Scaffold with `pp-pcf`
- Uses Microsoft's PAC infrastructure directly
- Referenced by solution projects — handled by standard PAC pipeline

### Package Deployer Projects (`ProjectType=PDPackage`)
- Scaffold with `pp-package`
- Orchestrates deployment of multiple solutions in order
- References solution projects — collects their output ZIPs for packaging

## Project References (`dotnet add reference`)

Solution projects act as the hub. When you add a project reference, the Build SDK **auto-detects the referenced project's type** via the `GetProjectType` MSBuild protocol and handles it accordingly:

```bash
# dotnet add <solution-project> reference <referenced-project> (names are the default layout's examples; use the repository's own):
dotnet add src/Solutions.Logic reference src/Plugins.Warehouse/Plugins.Warehouse.csproj            # Plugin → generates assembly data.xml
dotnet add src/Solutions.UI reference src/Scripts.UI/Scripts.UI.csproj                      # ScriptLibrary → generates web resource
dotnet add src/Solutions.Logic reference src/WorkflowActivities/WorkflowActivities.csproj           # WorkflowActivity → generates assembly data.xml
dotnet add src/Solutions.UI reference src/Solutions.DataModel/Solutions.DataModel.csproj             # Solution → dependency chain
```

For the project types it auto-detects, the build generates the registration data during `dotnet build`; manual steps are needed only in the cases listed in [plugin-development](plugin-development.md).

## Key Tools

| Command | Purpose |
|---|---|
| `txc component type list` | List available component types for scaffolding |
| `txc workspace component create <type> --output <dir>` | Scaffold new components into a solution project (add `--param <key>=<value>` per parameter) |

## Naming Conventions

These are recommended defaults.

- **Entity logical names**: lowercase with publisher prefix (e.g., `prefix_warehouseitem`)
- **Display names**: Proper case (e.g., `Warehouse Item`)
- **Publisher prefix**: Recommended convention is lowercase alphanumeric and 5 characters or fewer for consistency
- **Branch naming**: follow the repository's existing branch naming

## Getting Started

1. Read the solution file (`.slnx`, or `.sln`) with `dotnet sln list` and the projects' `ProjectType` to see what's already in the repo
2. Identify which solution project your changes belong to
3. Use `txc workspace component create <type> --output <dir> --param <key>=<value>` to scaffold (`--output` is required; `txc workspace component parameter list <type>` shows the parameters)
4. Build locally to validate before deploying

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
