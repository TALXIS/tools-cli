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

**Download and deploy a Dataverse package from NuGet:**
```sh
txc deploy package TALXIS.Controls.FileExplorer.Package \
  --version 0.0.0.10 \
  --environment "https://contoso.crm.dynamics.com"
```

### Deploying packages and solutions

The `txc deploy` group covers both execution and inspection of deployments against a Dataverse environment:

- `txc deploy package` — runs a Package Deployer package (NuGet name or local `.pdpkg.zip`).
- `txc deploy solution` — imports a single solution zip using the modern `Microsoft.PowerPlatform.Dataverse.Client` SDK; supports install, update, and single-step managed upgrade (`--stage-and-upgrade`).
- `txc deploy list` — lists recent runs across both package and solution streams with status and duration.
- `txc deploy show <id>` — shows details and findings for a single run, resolved by `latest`, an 8+ character GUID prefix, a full GUID, or a unique/solution name.

> [!IMPORTANT]
> `txc deploy solution`, `txc deploy list`, and `txc deploy show` run on the modern `Microsoft.PowerPlatform.Dataverse.Client` SDK, so they work natively on **Linux and macOS** as well as Windows — no Windows-only .NET Framework tooling required.

**Deploy a package from NuGet:**
```sh
txc deploy package TALXIS.Controls.FileExplorer.Package --environment https://org.crm.dynamics.com
```

**Import a single solution zip:**
```sh
txc deploy solution ./Solutions/MySolution_managed.zip --environment https://org.crm.dynamics.com
```

**List the 20 most recent runs:**
```sh
txc deploy list --environment https://org.crm.dynamics.com
```

**List problems from the last 7 days:**
```sh
txc deploy list --environment https://org.crm.dynamics.com --since 7d --problems
```

### Inspecting a deployment

`txc deploy show <id>` accepts a compact `<id>`:

- `latest` — the most recent run across both streams.
- An 8+ character GUID prefix (e.g. `9de18071`) — resolved against packages first, then solutions.
- A full GUID — for unambiguous lookup.
- A unique/solution name — falls back to the latest matching run.

Output is a compact summary by default, plus **findings** (remediation guidance such as overwrite-customizations warnings, install+upgrade pattern detection, stale `In Process` status, and slowest-solution hints). Pass `--full` to include every correlated solution and the formatted import log. Pass `--json` for a machine-readable payload.

**Show the latest run:**
```sh
txc deploy show latest --environment https://org.crm.dynamics.com
```

**Show a specific run by GUID prefix:**
```sh
txc deploy show 9de18071 --environment https://org.crm.dynamics.com
```

**Show a standalone solution import by name (with formatted log):**
```sh
txc deploy show MySolution --environment https://org.crm.dynamics.com --full
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
