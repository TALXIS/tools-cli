# TALXIS CLI (`txc`)

> [!WARNING]
> This project is currently in a development phase and not ready for production use.
> While we actively use these tools internally, our aim is to share and collaborate with the broader community to refine and enhance their capabilities.
> We are in the process of gradually open-sourcing the code, removing internal dependencies to make it universally applicable.
> At this stage, it serves as a source of inspiration and a basis for collaboration.
> We welcome feedback, suggestions, and contributions through pull requests.
>
> If you wish to use this project for your team, please contact us at hello@networg.com for a personalized onboarding experience and customization to meet your specific needs.

> [!TIP]
> **MCP Server Support:**  
> You can also use this CLI as a Model Context Protocol (MCP) server by installing the related .NET tool `txc-mcp`.  
> This enables integration with tools and workflows that support the MCP standard.  
> For setup and usage instructions, see [`TALXIS.CLI.MCP`](src/TALXIS.CLI.MCP/README.md).

---

TALXIS CLI (`txc`) is a modular, extensible .NET global tool for automating development, data, and solution management tasks - especially for Power Platform and enterprise projects. It helps developers scaffold, transform, and manage code and data in local repositories.

---

## Table of Contents
- [Installation](#installation)
- [Identity, Connections & Profiles](#identity-connections--profiles)
- [Example Usage](#example-usage)
- [Local Development & Debugging](#local-development--debugging)
- [Versioning & Release](#versioning--release)
- [Collaboration](#collaboration)

**Detailed guides:**
[Schema Management](docs/schema-management.md) · [Changeset Staging](docs/changeset-staging.md) · [Architecture](docs/architecture.md) · [Profiles & Auth](docs/profiles-and-authentication.md) · [Output Contract](docs/output-contract.md)

---

## Installation

**Install the CLI as a .NET global tool:**
```sh
dotnet tool install --global TALXIS.CLI
```

**Update to the latest version:**
```sh
dotnet tool update --global TALXIS.CLI

dotnet new update
```
> [!TIP]
> Running `dotnet new update` ensures that template packages used by the CLI are updated to their latest versions.

After installation, use the CLI via the `txc` command in any terminal.

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
> `txc` runs both **Dataverse Package Deployer** and **Configuration Migration Tool (CMT)** on **modern .NET**, including **macOS** and **Linux**. The goal is a better developer experience: cross-platform automation, simpler happy-path commands, and better visibility into what happened during deploys.

The examples below assume you have an active profile (see [above](#identity-connections--profiles)). Pass `--profile <name>` to any command to override the active profile for a single invocation.

`txc` organises environment operations into three planes, mirroring how the Power Platform itself separates concerns:

| Plane | What it covers | API surface | Commands |
|-------|---------------|-------------|----------|
| **Control plane** | Environment-level governance & feature toggles | `api.powerplatform.com/environmentmanagement` | `txc env setting …` |
| **Application plane** | Solutions, packages, deployments, schema management | Dataverse Web API + SDK + Package Deployer | `txc env sln …`, `txc env pkg …`, `txc env deploy …`, `txc env entity …` |
| **Data plane** | Records, queries, bulk operations, CMT data import/export | Dataverse Web API (OData / FetchXML / SQL) | `txc env data …`, `txc data …` |

### Control Plane

Manage environment-level settings exposed by the Power Platform admin API — feature toggles, Copilot flags, IP restrictions, and more.

**List environment management settings:**
```sh
txc env setting list --filter powerApps
```

**Enable Power Apps Code Apps:**
```sh
txc env setting update --name powerApps_AllowCodeApps --value true
```

### Application Plane

Deploy, inspect, and manage solutions and packages in the target environment.

**Deploy the latest package from NuGet:**
```sh
txc env pkg import TALXIS.Controls.FileExplorer.Package
```

**Inspect the latest package deployment with findings:**
```sh
txc env deploy show --package-name TALXIS.Controls.FileExplorer.Package
```

**Uninstall a package from its source artifact:**
```sh
txc env pkg uninstall TALXIS.Controls.FileExplorer.Package --yes
```

**Import a solution and follow the async operation when needed:**
```sh
txc env sln import ./Solutions/MySolution_managed.zip

txc env deploy show --async-operation-id <asyncOperationId>
```

**Target a different environment for a single call without switching profiles:**
```sh
txc env sln import ./Solutions/MySolution_managed.zip --profile customer-b-prod
```

### Data Plane

Query, create, update, and bulk-operate on Dataverse records. Three query languages, full record CRUD with file/image columns, N:N relationship management, and Configuration Migration Tool (CMT) data import/export — all cross-platform on modern .NET.

```sh
# Query with OData, FetchXML, or SQL — your choice
txc env data query odata accounts --select "name,revenue" --filter "revenue gt 1000000" --top 10
txc env data query fetchxml '<fetch top="5"><entity name="contact"><attribute name="fullname"/></entity></fetch>'
txc env data query sql "SELECT fullname, emailaddress1 FROM contact WHERE statecode = 0" --top 20

# Full record lifecycle
txc env data record get --entity account $ID --columns "name,revenue"
txc env data record create --entity account --data '{"name":"Contoso Ltd","revenue":5000000}' --apply
txc env data record update --entity account $ID --data '{"name":"Contoso International"}' --apply
txc env data record delete --entity account $ID --yes --apply

# File & image columns — chunked streaming, not memory-bound
txc env data record upload-file --entity account $ID --column logo --file ./logo.png --apply
txc env data record download-file --entity account $ID --column logo --output ./logo.png

# N:N relationship management
txc env data record associate $ID --entity account \
  --target $TARGET_ID --target-entity contact --relationship accountleads_association --apply

# Bulk operations
txc env data bulk upsert --entity contact --file ./contacts.json
```

**Configuration Migration Tool (CMT)** — import, export, convert. Runs CMT natively on macOS/Linux (no Windows VM needed):

```sh
txc data pkg export --schema ./data_schema.xml --output ./export.zip --export-files
txc data pkg import ./data-package --batch-mode --batch-size 500 --connection-count 4
txc data pkg convert --input export.xlsx --output data.xml
```

See [docs/configuration-migration.md](docs/configuration-migration.md) for tuning options and deep-dive into CMT internals.

### Application Plane — Schema Management

Define your Dataverse schema from the terminal — entities, columns, relationships, option sets. Every mutating command supports `--apply` (execute now) or `--stage` (queue for batch). See [docs/schema-management.md](docs/schema-management.md).

```sh
# Spin up a new entity with a money column in seconds
txc env entity create --name tom_project \
  --display-name "Project" --plural-name "Projects" \
  --ownership user --apply

txc env entity attribute create --entity tom_project \
  --name tom_budget --type money --display-name "Budget" --apply

# Or stage everything and apply in one optimised batch
txc env entity create --name tom_invoice \
  --display-name "Invoice" --plural-name "Invoices" --stage
txc env entity attribute create --entity tom_invoice \
  --name tom_amount --type money --display-name "Amount" --stage
txc env entity attribute create --entity tom_invoice \
  --name tom_duedate --type datetime --display-name "Due Date" --stage

txc env changeset status          # review what's queued
txc env changeset apply --strategy batch   # one batch, one publish
```

Changeset staging batches entity creation via the `CreateEntities` SDK action and consolidates all publishes into a single `PublishXml` call — dramatically faster than sequential operations. See [docs/changeset-staging.md](docs/changeset-staging.md).

### Workspace Scaffolding

Scaffold and manage project components from templates.

**List available workspace components:**
```sh
txc workspace component type list
```

> [!IMPORTANT]
> Component scaffolding in this CLI relies on the [TALXIS/tools-devkit-templates](https://github.com/TALXIS/tools-devkit-templates) repository, where all component types, metadata, and definitions are maintained.

**Show details about a component:**
```sh
txc workspace component explain pp-entity
```

**List parameters required for a specific component template:**
```sh
txc workspace component parameter list pp-entity
```

**Scaffold a Dataverse entity component:**
```sh
txc workspace component create pp-entity \
  --output "/Users/tomasprokop/Desktop/mcp-test/test" \
  --param Behavior=New \
  --param PublisherPrefix=tom \
  --param LogicalName=location \
  --param LogicalNamePlural=locations \
  --param DisplayName=Location \
  --param DisplayNamePlural=Locations \
  --param SolutionRootPath=Declarations
```

> [!NOTE]
> Run `txc --help` or `txc <command> --help` for the full command reference.

---

## Local Development & Debugging

**Clone the repository and restore dependencies:**
```sh
git clone <repo-url>
cd tools-cli
dotnet restore
```

**Build the solution:**
```sh
dotnet build
```

**Run the CLI directly:**
```sh
dotnet run --project src/TALXIS.CLI -- workspace explain
```

---

## Versioning & Release

- Versioning is managed in `Directory.Build.props` (Microsoft-style versioning).
- Releases are published to NuGet.org via GitHub Actions.

---

## Collaboration

We welcome collaboration! For feedback, suggestions, or contributions, please submit issues or pull requests.

For onboarding or customization, contact us at hello@networg.com.
