# TALXIS DevKit CLI (`txc`)

> [!WARNING]
> This project is currently in a development phase and not ready for production use.
> While we actively use these tools internally, our aim is to share and collaborate with the broader community to refine and enhance their capabilities.
> We are in the process of gradually open-sourcing the code, removing internal dependencies to make it universally applicable.
> At this stage, it serves as a source of inspiration and a basis for collaboration.
> We welcome feedback, suggestions, and contributions through pull requests.
>
> If you wish to use this project for your team, please contact us at hello@networg.com for a personalized onboarding experience and customization to meet your specific needs.

---

TALXIS DevKit CLI (`txc`) is a code-first toolkit for Power Platform and Dataverse development. Scaffold components locally, validate with builds, and synchronize to a live environment.

## Getting Started

### Prerequisites

- [**.NET 10 SDK**](https://dotnet.microsoft.com/download/dotnet/10.0) or later (`dotnet --version` should show `10.0.x`+)
- [**PowerShell**](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) (`pwsh`) — required by template post-action scripts
- [**GitHub Copilot CLI**](https://github.com/features/copilot/cli) (or an alternative AI harness like Claude Code) for MCP

### MCP Server (recommended)

The easiest way to use TALXIS DevKit is through the **MCP server** with GitHub Copilot or another AI assistant. No manual CLI commands needed — the AI discovers and calls tools for you.

**→ [MCP Server setup instructions](src/TALXIS.CLI.MCP/README.md)**

### CLI (advanced)

For scripting, CI/CD pipelines, or when you prefer manual control:

```sh
# Run without installing (dnx ships with .NET 10 SDK)
dnx TALXIS.CLI -- workspace explain

# Or install as a global tool
dotnet tool install --global TALXIS.CLI
txc workspace explain
```

---

## Table of Contents
- [Identity, Connections & Profiles](#identity-connections--profiles)
- [Example Usage](#example-usage)
- [Local Development & Debugging](#local-development--debugging)
- [Versioning & Release](#versioning--release)
- [Collaboration](#collaboration)

**Detailed guides:**
[Schema Management](docs/schema-management.md) · [Changeset Staging](docs/changeset-staging.md) · [Architecture](docs/architecture.md) · [Profiles & Auth](docs/profiles-and-authentication.md) · [Output Contract](docs/output-contract.md)

---

## Identity, Connections & Profiles

`txc` decouples **who you are** (credentials) from **where you target** (connections) and exposes the combination as a named **profile**. Every command that touches a live environment takes exactly one context flag — `--profile <name>`.

**Quickstart for most developers:**

```sh
txc config profile create --url https://contoso.crm4.dynamics.com/
```

Drop in the Dataverse environment URL, sign in in the browser, and `txc` creates and selects the profile for you.

For explicit credential / connection / profile steps, repository pinning, or headless / CI setup, see [docs/profiles-and-authentication.md](docs/profiles-and-authentication.md).

---

## Example Usage

> [!IMPORTANT]
> `txc` runs on **modern .NET** across **macOS**, **Linux**, and **Windows** — including Dataverse Package Deployer and Configuration Migration Tool (CMT), which traditionally require Windows.

`txc` commands fall into two layers:

| Layer | Purpose | Speed | Commands |
|-------|---------|-------|----------|
| **Workspace** | Scaffold & manage components locally in your repo | Instant (local) | `txc workspace …` |
| **Environment** | Synchronize with and operate on a live Dataverse environment | Minutes | `txc env …`, `txc data …` |

**The recommended workflow:** Use `txc workspace` to create and modify components locally (entities, attributes, solution structures), then deploy to the environment with `txc env`. This is dramatically faster than round-tripping every change through a live org — especially for coding agents that make dozens of changes per session.

The environment layer is organised into three planes:

| Plane | What it covers | Commands |
|-------|---------------|----------|
| **Control** | Environment settings, feature toggles, governance | `txc env setting …` |
| **Application** | Solutions, packages, deployments, schema management | `txc env sln …`, `txc env pkg …`, `txc env deploy …`, `txc env entity …` |
| **Data** | Records, queries, bulk operations, CMT import/export | `txc env data …`, `txc data …` |

### Workspace — Local-First Development

The fastest way to build Dataverse components. Everything happens locally in your repo — no environment round-trips, no publish waits. Ideal for coding agents that need to scaffold dozens of components in a session.

```sh
# Explore available component types and their parameters
txc workspace component type list
txc workspace component explain pp-entity

# Scaffold a Dataverse entity — instant, local, no environment needed
txc workspace component create pp-entity \
  --param Behavior=New \
  --param PublisherPrefix=tom \
  --param LogicalName=location \
  --param DisplayName=Location \
  --param DisplayNamePlural=Locations

# When ready, deploy the solution to a live environment
txc env sln import ./out/MySolution_managed.zip
```

> [!IMPORTANT]
> Component scaffolding relies on the [TALXIS/tools-devkit-templates](https://github.com/TALXIS/tools-devkit-templates) repository, where all component types, metadata, and definitions are maintained.

The environment commands below assume you have an active profile (see [above](#identity-connections--profiles)). Pass `--profile <name>` to override for a single call.

### Control Plane

Manage environment-level settings exposed by the Power Platform admin API — feature toggles, Copilot flags, IP restrictions, and more.

**List environment management settings:**
```sh
txc env setting list --filter powerApps
```

**Enable Power Apps Code Apps:**
```sh
txc env setting update powerApps_AllowCodeApps true
```

### Application Plane

Deploy, inspect, and manage solutions and packages in the target environment.

```sh
# Deploy a package straight from NuGet, inspect the result
txc env pkg import TALXIS.Controls.FileExplorer.Package
txc env deploy show --package-name TALXIS.Controls.FileExplorer.Package

# Import a solution, target a different environment for one call
txc env sln import ./Solutions/MySolution_managed.zip --profile customer-b-prod

# Uninstall a package cleanly
txc env pkg uninstall TALXIS.Controls.FileExplorer.Package --yes
```

**Solution round-tripping and component inspection:**

```sh
# Import from a folder or .cdsproj project — auto-packs via SolutionPackager
txc env sln import ./src/MySolution/

# Publish customizations after import (makes forms, views, sitemaps visible)
txc env sln publish

# Export, unpack, edit locally, pack, re-import
txc env sln export MySolution --output ./export/MySolution.zip --zip
txc env sln unpack ./export/MySolution.zip --output ./src/MySolution/
txc env sln pack ./src/MySolution/ --output ./out/MySolution.zip

# Inspect solution metadata and component breakdown
txc env sln show MySolution
txc env sln component list MySolution --type entity

# Drill into component layers and dependencies by name — no GUIDs needed
txc env component layer list --entity account --attribute revenue
txc env component dep delete-check --entity tom_project
```

### Data Plane

Query, create, update, and bulk-operate on Dataverse records — all cross-platform on modern .NET.

**Three query languages — pick the one you think in:**

```sh
# OData — familiar, filterable, composable
txc env data query odata accounts --select "name,revenue" --filter "revenue gt 1000000" --top 10

# FetchXML — full aggregation, linked entities, fiscal date filters
txc env data query fetchxml '<fetch top="5"><entity name="contact"><attribute name="fullname"/></entity></fetch>'

# T-SQL — because sometimes you just want SELECT ... WHERE
txc env data query sql "SELECT fullname, emailaddress1 FROM contact WHERE statecode = 0" --top 20
```

**Record CRUD, file columns, relationships, bulk:**

```sh
txc env data record create --entity account --data '{"name":"Contoso Ltd","revenue":5000000}' --apply
txc env data record upload-file --entity account $ID --column logo --file ./logo.png --apply
txc env data record associate $ID --entity account \
  --target $TARGET_ID --target-entity contact --relationship accountleads_association --apply
txc env data bulk upsert --entity contact --file ./contacts.json   # CreateMultiple/UpsertMultiple under the hood
```

**Configuration Migration Tool (CMT)** — import, export, convert. Runs natively on macOS/Linux (no Windows VM needed). Exports to a folder by default so you can commit data directly to your repo:

```sh
# Export → folder → edit → import round-trip
txc data pkg export --schema ./data_schema.xml --output ./data-package --export-files
txc data pkg import ./data-package

# Advanced tuning options not exposed by PAC CLI or CMT GUI:
txc data pkg import ./data-package \
  --batch-mode                  # ExecuteMultiple batching (vs one-by-one) \
  --batch-size 500              # records per batch (default: 200) \
  --connection-count 4          # parallel service channels \
  --override-safety-checks      # skip duplicate detection \
  --prefetch-limit 100          # pre-cache record lookups

txc data pkg convert --input export.xlsx --output data.xml
```

See [docs/configuration-migration.md](docs/configuration-migration.md) for the full deep-dive into CMT internals, deduplication logic, and tuning strategies.

### Application Plane — Schema Management

Define your Dataverse schema from the terminal — entities, columns, relationships, option sets. Every mutating command supports `--apply` (execute now) or `--stage` (queue for batch). See [docs/schema-management.md](docs/schema-management.md).

```sh
# Spin up a new entity with a money column in seconds
txc env entity create --name tom_project \
  --display-name "Project" --plural-name "Projects" \
  --ownership user --apply

txc env entity attribute create tom_project \
  --name tom_budget --type money --display-name "Budget" --apply

# Or stage everything and apply in one optimised batch
txc env entity create --name tom_invoice \
  --display-name "Invoice" --plural-name "Invoices" --stage
txc env entity attribute create tom_invoice \
  --name tom_amount --type money --display-name "Amount" --stage
txc env entity attribute create tom_invoice \
  --name tom_duedate --type datetime --display-name "Due Date" --stage

txc env changeset status          # review what's queued
txc env changeset apply --strategy batch   # one batch, one publish
```

Changeset staging batches entity creation via the `CreateEntities` SDK action and consolidates all publishes into a single `PublishXml` call — dramatically faster than sequential operations. See [docs/changeset-staging.md](docs/changeset-staging.md).

> [!NOTE]
> Run `txc --help` or `txc <command> --help` for the full command reference.

---

## Local Development & Debugging

**Clone and build:**
```sh
git clone https://github.com/TALXIS/tools-cli.git
cd tools-cli
dotnet build
```

**Run the CLI directly:**
```sh
dotnet run --project src/TALXIS.CLI -- workspace explain
```

**Run the MCP server locally:**
```sh
dotnet run --project src/TALXIS.CLI.MCP
```

### Working with all three repos locally

The CLI, templates, and build SDK are separate packages. To test changes across all three:

```sh
# 1. Clone all repos side by side
git clone https://github.com/TALXIS/tools-cli.git
git clone https://github.com/TALXIS/tools-devkit-templates.git
git clone https://github.com/TALXIS/tools-devkit-build.git
```

**Local templates** — pack and add as a local NuGet source so the CLI's template engine finds them:
```sh
cd tools-devkit-templates
dotnet pack --configuration Debug
```

**Local build SDK** — same approach:
```sh
cd tools-devkit-build
dotnet pack --configuration Debug
```

**Configure a local NuGet source** — add a `nuget.config` at the workspace root (or use `dotnet nuget add source`):
```xml
<configuration>
  <packageSources>
    <add key="LocalTemplates" value="/path/to/tools-devkit-templates/src/Dataverse/bin/Debug/" />
    <add key="LocalBuildSdk" value="/path/to/tools-devkit-build/src/Dataverse/Tasks/bin/Debug/" />
  </packageSources>
</configuration>
```

**Local CLI** — run directly from source:
```sh
cd tools-cli
dotnet run --project src/TALXIS.CLI -- <command>
```

**Cleanup** — remove local sources and clear cache to revert to published packages:
```sh
dotnet nuget locals all --clear
```

---

## Versioning & Release

Releases are published through [GitHub Releases](https://github.com/TALXIS/tools-cli/releases):

1. Go to **Releases** → **Draft a new release**
2. Create a tag in the format `vX.Y.Z` (e.g. `v1.12.0`)
3. Write the changelog in the release body
4. Click **Publish release**

The publish workflow runs tests, builds NuGet packages with the tag version, and pushes them to [nuget.org](https://www.nuget.org/packages/TALXIS.CLI). Release notes from all GitHub releases are embedded in the NuGet package.

The same process applies to [tools-devkit-templates](https://github.com/TALXIS/tools-devkit-templates) and [tools-devkit-build](https://github.com/TALXIS/tools-devkit-build).

---

## Collaboration

We welcome collaboration! For feedback, suggestions, or contributions, please submit issues or pull requests.

For onboarding or customization, contact us at hello@networg.com.
