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

`txc` focuses on deployment, data operations, and workspace scaffolding.

**Noteworthy capabilities**
- Package and solution deployment (`txc deploy run`)
- Deployment inspection and troubleshooting findings (`txc deploy list/show`)
- Solution/package uninstall, including source-based package uninstall (`txc deploy uninstall`)
- Dataverse data package import/convert (`txc data package import|convert`)
- Dataverse model export/convert (`txc data model convert`)
- Power Platform workspace scaffolding (`txc workspace component ...`)

**Sample: deploy package from NuGet**
```sh
txc deploy run --type package --source TALXIS.Controls.FileExplorer.Package \
  --version 0.0.0.10 \
  --environment https://org.crm.dynamics.com
```

**Sample: import solution and track async operation**
```sh
txc deploy run --type solution --source ./Solutions/MySolution_managed.zip \
  --environment https://org.crm.dynamics.com

txc deploy show --id <asyncOperationId> --environment https://org.crm.dynamics.com
```

**Sample: uninstall package in reverse import order from source**
```sh
txc deploy uninstall --package-source TALXIS.Controls.FileExplorer.Package \
  --version 0.0.0.10 \
  --force \
  --environment https://org.crm.dynamics.com
```

**Sample: import CMT data package**
```sh
txc data package import ./data-package --environment https://org.crm.dynamics.com
```

**Sample: list available workspace components**
```sh
txc workspace component type list
```

> [!IMPORTANT]
> Component scaffolding in this CLI relies on the [TALXIS/tools-devkit-templates](https://github.com/TALXIS/tools-devkit-templates) repository, where all component types, metadata, and definitions are maintained.

**Sample: scaffold a Dataverse entity component**
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
> Run `txc --help` or `txc <command> --help` for full command reference.

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
