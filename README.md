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
- [Example Usage](#example-usage)
- [Local Development & Debugging](#local-development--debugging)
- [Versioning & Release](#versioning--release)
- [Collaboration](#collaboration)

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

## Example Usage

> [!IMPORTANT]
> `txc` runs both **Dataverse Package Deployer** and **Configuration Migration Tool (CMT)** on **modern .NET**, including **Linux** and **macOS**. This makes it possible to run deployment and data import flows outside the traditional Windows-only .NET Framework tooling.

**Run a package deployment from NuGet:**
```sh
txc deploy run --type package --source TALXIS.Controls.FileExplorer.Package \
  --version 0.0.0.10 \
  --environment "https://contoso.crm.dynamics.com"
```

### Deploying packages and solutions

The `txc deploy` group uses one verb per action:

- `txc deploy run` — runs a package or solution deployment (`--type package|solution`).
- `txc deploy list` — lists either deployment runs or installed solutions (`--resource runs|solutions`).
- `txc deploy show` — shows details and findings for a single run; specify the target with `--id`, `--package-name`, `--solution-name`, or `--latest`.
- `txc deploy uninstall` — uninstalls a single solution or all correlated solutions from a package run.

> [!IMPORTANT]
> `txc deploy run --type solution`, `txc deploy list`, `txc deploy show`, and `txc deploy uninstall` run on the modern `Microsoft.PowerPlatform.Dataverse.Client` SDK, so they work natively on **Linux and macOS** as well as Windows — no Windows-only .NET Framework tooling required.

**Run a package deployment from NuGet:**
```sh
txc deploy run --type package --source TALXIS.Controls.FileExplorer.Package --environment https://org.crm.dynamics.com
```

**Run a solution import (async, returns immediately):**
```sh
txc deploy run --type solution --source ./Solutions/MySolution_managed.zip --environment https://org.crm.dynamics.com
# → AsyncOperationId: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx

# Block until import completes:
txc deploy run --type solution --source ./Solutions/MySolution_managed.zip --wait --environment https://org.crm.dynamics.com
```

**List the 20 most recent deployment runs:**
```sh
txc deploy list --resource runs --environment https://org.crm.dynamics.com
```

**List problems from the last 7 days:**
```sh
txc deploy list --resource runs --environment https://org.crm.dynamics.com --since 7d --problems
```

**List installed solutions and versions:**
```sh
txc deploy list --resource solutions --environment https://org.crm.dynamics.com
```

### Inspecting a deployment

`txc deploy show` resolves a run with exactly one of:

- `--latest` — the most recent run across both streams.
- `--id <guid>` — a deployment record GUID or the `asyncOperationId` returned by a queued solution import. If the operation is still in progress, live status is shown.
- `--package-name <name>` — the NuGet package name used to deploy (e.g. `TALXIS.Controls.FileExplorer.Package`). Reliable for packages deployed via `txc deploy run --type package`.
- `--solution-name <name>` — the solution unique name for a standalone solution import.

Output is a compact summary by default, plus **findings** (remediation guidance such as overwrite-customizations warnings, install+upgrade pattern detection, stale `In Process` status, and slowest-solution hints). Pass `--full` to include every correlated solution and the formatted import log. Pass `--json` for a machine-readable payload.

**Show the latest run:**
```sh
txc deploy show --latest --environment https://org.crm.dynamics.com
```

**Track a queued import by asyncOperationId:**
```sh
txc deploy show --id xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx --environment https://org.crm.dynamics.com
```

**Show a specific package run by NuGet name:**
```sh
txc deploy show --package-name TALXIS.Controls.FileExplorer.Package --environment https://org.crm.dynamics.com
```

**Show a standalone solution import by name (with formatted log):**
```sh
txc deploy show --solution-name MySolution --environment https://org.crm.dynamics.com --full
```

### Uninstalling solutions

**Uninstall a single solution:**
```sh
txc deploy uninstall --solution-name MySolution --yes --environment https://org.crm.dynamics.com
```

**Uninstall all solutions correlated to the latest package run:**
```sh
txc deploy uninstall --package-name TALXIS.Controls.FileExplorer.Package --latest --yes --environment https://org.crm.dynamics.com
```

**Import a CMT data folder into Dataverse:**
```sh
txc data package import ./data-package \
  --environment "https://contoso.crm.dynamics.com"
```

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

**Scaffold a new Dataverse entity component:**
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

**Convert Excel to CMT XML:**
```sh
txc data package convert --input export.xlsx --output data.xml
```

> [!NOTE]
> For detailed usage instructions, run `txc --help` or `txc <command> --help` in your terminal.

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
