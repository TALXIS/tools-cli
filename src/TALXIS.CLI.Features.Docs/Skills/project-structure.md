# Workspace & Project Organization

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## Overview

A Power Platform monorepo follows a structured layout where solution projects, plugin projects, and package deployer projects coexist. To see what a repository already contains, read its solution file (`.slnx`, or `.sln` in older repos) and each project's `ProjectType`. `dotnet sln list` lists the projects of the solution file, and searching the `.csproj` files for `<ProjectType>` (for example `grep -r "<ProjectType>" --include=*.csproj .`) shows each one's type.

## Repository Structure Convention

This is our default layout. Precedence: what the request says first, then what the repository already has (existing project names, folders and solution file are respected, never renamed or assumed), then these defaults. By default every project lives under `src/` as an SDK-style `.csproj`, and all of them are listed in one solution file at the repository root (`.slnx`; older repos use `.sln`). Default project names are by role: `Solutions.*` (for example `Solutions.DataModel`, `Solutions.Logic`, `Solutions.UI`, `Solutions.Security`), `Plugins.{Domain}`, `Scripts.UI`, `Packages.Main`, and test projects as `src/Tests.*`. CI/CD workflows live in `.github/workflows/`. The names are a convention, not a requirement: what the build cares about is each project's `ProjectType` (below) and its references.

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
- Scaffold with `pp-plugin`, reference it from the solution project that holds the registrations (`dotnet add <solution-project> reference <plugin-project>`) and build that solution project (`dotnet build <solution-project>`), not just the plugin. The build registers the plugin assembly in the referencing solution (`GeneratePluginAssembly` defaults to true), so there is no `pp-plugin-assembly` step; running it after that build adds a second assembly root and the next build fails. Steps come from `[PluginStep]` attributes on the classes (the same build generates them); `pp-plugin-assembly-step` is only for what attributes cannot express or an older Build SDK. See [plugin-development](plugin-development.md).
- .NET class libraries containing plugin classes that extend `PluginBase`
- Referenced by solution projects — Build SDK auto-generates plugin assembly data.xml when that solution project is built

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
# From a solution project directory (names are examples; use the repository's own):
dotnet add reference ../Plugins.Warehouse/Plugins.Warehouse.csproj      # Plugin → auto-generates assembly data.xml
dotnet add reference ../Scripts.UI/Scripts.UI.csproj                    # ScriptLibrary → auto-generates web resource
dotnet add reference ../WorkflowActivities/WorkflowActivities.csproj     # WorkflowActivity → auto-generates assembly data.xml
dotnet add reference ../Solutions.DataModel/Solutions.DataModel.csproj    # Solution → dependency chain
```

All auto-generation happens during `dotnet build` — no manual registration steps needed.

## Key Tools

| Command | Purpose |
|---|---|
| `txc component type list` | List available component types for scaffolding |
| `txc workspace component create <type> --output <dir>` | Scaffold new components into a solution project (add `--param <key>=<value>` per parameter) |

## Naming Conventions

These are recommended defaults. What the request says wins, then the repository's existing conventions.

- **Entity logical names**: lowercase with publisher prefix (e.g., `prefix_warehouseitem`)
- **Display names**: Proper case (e.g., `Warehouse Item`)
- **Publisher prefix**: Recommended convention is lowercase alphanumeric and 5 characters or fewer for consistency
- **Branch naming**: `users/<name>/<topic>` (trunk-based development)

## Getting Started

1. Read the solution file (`.slnx`, or `.sln`) with `dotnet sln list` and the projects' `ProjectType` to see what's already in the repo
2. Identify which solution project your changes belong to
3. Use `txc workspace component create <type> --output <dir> --param <key>=<value>` to scaffold (`--output` is required; `txc workspace component parameter list <type>` shows the parameters)
4. Build locally to validate before deploying

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
