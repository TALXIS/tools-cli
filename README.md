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
> You can also use this CLI as a Model Context Protocol (MCP) server
> by installing the related .NET tool `txc-mcp`.  
> 
> This enables integration with tools and workflows that support the MCP standard.  
> For setup and usage instructions, see [`TALXIS.CLI.MCP`](src/TALXIS.CLI.MCP/README.md).

---

TALXIS CLI (`txc`) is a modular, extensible .NET global tool for automating development, data, and solution management tasks—especially for Power Platform and enterprise projects. It is designed to help developers scaffold, transform and manage code and data in local repositories.

---

## Table of Contents
- [Installation](#installation)
- [Example Usage](#example-usage)
- [Local Development & Debugging](#local-development--debugging)
- [Versioning & Release](#versioning--release)
- [Collaboration](#collaboration)

---

## Installation

```sh title="Install as a .NET global tool"
dotnet tool install --global TALXIS.CLI
```

```sh title="Update to the latest version"
dotnet tool update --global TALXIS.CLI
```

After installation, use the CLI via the `txc` command in any terminal.

---

## Example Usage

```sh title="Start the data transformation server on default port"
txc data transform server start
```

```sh title="Convert Excel to CMT XML"
txc data package convert --input export.xlsx --output data.xml
```

```sh title="List available workspace components"
txc workspace component list
```

> [!IMPORTANT]
> Component scaffolding in this CLI relies on the [TALXIS/tools-devkit-templates](https://github.com/TALXIS/tools-devkit-templates) repository, where all component types, metadata and definitions are maintained.

```sh title="Show details about a component template"
txc workspace component explain pp-entity
```

```sh title="List parameters required for a specific component template"
txc workspace component parameter list pp-entity
```

```sh title="Scaffold a new Dataverse entity component (important example)"
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
> For detailed usage instructions, run `txc --help` or `txc <command> --help` in your terminal.

---

## Local Development & Debugging

1. Clone the repository and restore dependencies:
   ```sh
   git clone <repo-url>
   cd tools-cli
   dotnet restore
   ```
2. Build the solution:
   ```sh
   dotnet build
   ```
3. Run the CLI directly (for example, to test the data transform server):
   ```sh
   dotnet run --project src/TALXIS.CLI -- data transform server start
   ```
4. Debug using Visual Studio or VS Code as needed.

---

## Versioning & Release

- Versioning is managed in `Directory.Build.props` (Microsoft-style versioning).
- Releases are published to NuGet.org via GitHub Actions.

---

## Collaboration

We welcome collaboration! For feedback, suggestions, or contributions, please submit issues or pull requests.

For onboarding or customization, contact us at hello@networg.com.
