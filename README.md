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
> `txc` runs both **Dataverse Package Deployer** and **Configuration Migration Tool (CMT)** on **modern .NET**, including **macOS** and **Linux**. The goal is a better developer experience: cross-platform automation, simpler happy-path commands, and better visibility into what happened during deploys.

**Deploy the latest package from NuGet:**
```sh
txc env pkg import TALXIS.Controls.FileExplorer.Package \
  --environment https://org.crm.dynamics.com
```

**Inspect the latest package deployment with findings:**
```sh
txc env deploy show --package-name TALXIS.Controls.FileExplorer.Package \
  --environment https://org.crm.dynamics.com
```

**Uninstall a package from its source artifact:**
```sh
txc env pkg uninstall TALXIS.Controls.FileExplorer.Package \
  --yes \
  --environment https://org.crm.dynamics.com
```

**Import a solution and follow the async operation when needed:**
```sh
txc env sln import ./Solutions/MySolution_managed.zip \
  --environment https://org.crm.dynamics.com

txc env deploy show --async-operation-id <asyncOperationId> \
  --environment https://org.crm.dynamics.com
```

**Import a CMT data folder into Dataverse:**
```sh
txc data pkg import ./data-package \
  --environment https://org.crm.dynamics.com
```

**Convert Excel to CMT XML:**
```sh
txc data package convert --input export.xlsx --output data.xml
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
