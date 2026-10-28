# Workspace & Project Organization

A Power Platform monorepo holds solution, plugin, script and package deployer projects side by side. To see what a repository contains, run `dotnet sln list` and search the `.csproj` (or legacy `.cdsproj`) files for `<ProjectType>`.

## Repository Structure

```
<solution file>.slnx          # .sln in older repos
.github/workflows/            # CI/CD
src/
  Solutions.DataModel/        # tables, columns, relationships
  Solutions.Logic/            # plugin steps, workflows
  Solutions.UI/               # forms, views, apps
  Solutions.Security/         # roles
  Plugins.{Domain}/           # e.g. Plugins.Warehouse
  Scripts.UI/                 # script library
  Packages.Main/              # package deployer
  Tests.*/                    # test projects
```

This is a default: the build cares about each project's `ProjectType` and references, not names. Existing projects, folders and the solution file are never renamed.

## Project Types

Each `.csproj` declares `<ProjectType>` and the Build SDK routes it to its build logic. Solution projects are the **hub**: reference each project from one (`dotnet add <solution-project> reference <project>.csproj`), then `dotnet build <solution-project>` generates its registration data.

| ProjectType | Scaffold | What it is; what the solution build generates |
|---|---|---|
| `Solution` | `pp-solution` | Dataverse solution XML, one project per Dataverse solution (default roles: DataModel = tables, columns, relationships, option sets; Logic = workflows, business rules, plugin steps; UI = forms, views, model-driven apps, sitemaps; Security = roles, field-level security profiles). Can reference other solution projects (dependency chain). |
| `Plugin` | `pp-plugin` | Classes extending `PluginBase`; see [plugin-development](plugin-development.md). |
| `ScriptLibrary` | `pp-script-library` (`PublisherPrefix`, `LibraryName`) | TypeScript + Rollup, one UMD web resource; its global `prefix_name` is the function namespace forms and ribbon buttons call (`prefix_name.Main.onLoad`). `dotnet build` runs npm and generates the web resource: no manual `pp-webresource` or npm. |
| `WorkflowActivity` | `pp-workflow-activity` | Custom workflow steps extending `WorkflowActivityBase`. Generates assembly data.xml, copies the DLL. |
| `CodeApp` | `pp-app-code` | Node/TypeScript code-first app. Runs the npm build, generates CanvasApp metadata. |
| `Pcf` | `pp-pcf` | PCF control, see [pcf-controls](pcf-controls.md). Handled by the standard PAC pipeline. |
| `PDPackage` | `pp-package` | Package deployer: references solution projects and packages their output ZIPs in order. |

`txc component type list` lists all templates; scaffold with `txc workspace component create <type> --output <dir> --param <key>=<value>` ([component-creation](component-creation.md)).

## Naming Conventions

- Entity logical names: lowercase with publisher prefix (`prefix_warehouseitem`); display names: proper case (`Warehouse Item`).
- Publisher prefix: lowercase alphanumeric, 5 characters or fewer.
- Branches: follow the repository's convention; otherwise `users/<name>/<topic>` (trunk-based).

See also: [component-creation](component-creation.md), [deployment-workflow](deployment-workflow.md)
