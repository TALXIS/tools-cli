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

`txc` decouples **who you are** (credentials) from **where you target** (connections) and exposes the combination as a named **profile**. Every command that touches a live environment takes exactly one context flag — `--profile <name>` (short form `-p`). There are no raw `--environment`, `--connection-string`, or `--device-code` flags on leaf commands; to switch endpoints or identities you create (or select) a different profile.

The resolution order for the active profile is:

```
--profile flag > TXC_PROFILE env > <repo>/.txc/workspace.json > global active pointer (~/.txc/config.json)
```

Credentials never live in config files. Service-principal secrets, PATs, and certificate passwords are stored in the OS credential vault (DPAPI on Windows, Keychain on macOS, libsecret on Linux) and referenced from `credentials.json` by opaque `vault://` handles. MSAL tokens live in a separate cache file protected by the same vault.

### Interactive workflow (dev laptop)

```sh
# 1. Log in interactively (opens a browser). Creates a Credential entry
#    aliased from your UPN (override with --alias) and primes the MSAL cache.
txc c auth login

# 2. Register the Dataverse environment you want to target.
txc c connection create customer-a-dev \
  --provider dataverse \
  --environment https://contoso.crm4.dynamics.com/

# 3. Bind credential + connection into a profile and select it.
txc c p create customer-a-dev \
  --auth <upn-alias> \
  --connection customer-a-dev
txc c p select customer-a-dev

# 4. Optional: pin this profile to the current repo.
#    Writes <repo>/.txc/workspace.json so every shell in this checkout
#    defaults to customer-a-dev without touching the global pointer.
txc c p pin
# Unpin when done:  txc c p unpin

# 5. Sanity-check end-to-end auth + endpoint reachability.
txc c p validate
```

### Headless / CI workflow (service principal)

```sh
# Total config isolation — nothing written to $HOME on the runner.
export TXC_CONFIG_DIR="$RUNNER_TEMP/txc-config"
export TXC_NON_INTERACTIVE=1

# Secret is supplied via env var (never as --secret on the command line,
# which would leak to shell history and process listings).
export SPN_SECRET='<client-secret>'

txc c auth add-service-principal \
  --tenant "$AZURE_TENANT_ID" \
  --client-id "$AZURE_CLIENT_ID" \
  --alias ci-spn \
  --secret-from-env SPN_SECRET

txc c connection create ci-target \
  --provider dataverse \
  --environment "$DATAVERSE_URL"

txc c p create ci --auth ci-spn --connection ci-target
txc c p select ci

# Every subsequent txc call picks up TXC_CONFIG_DIR + the selected profile.
txc env pkg import TALXIS.Controls.FileExplorer.Package
```

For workload-identity federation (GitHub OIDC, Azure DevOps WIF), the Dataverse provider auto-detects `ACTIONS_ID_TOKEN_REQUEST_*` and `TXC_ADO_ID_TOKEN_REQUEST_*` env vars at acquire time — no extra flags needed.

---

## Example Usage

> [!IMPORTANT]
> `txc` runs both **Dataverse Package Deployer** and **Configuration Migration Tool (CMT)** on **modern .NET**, including **macOS** and **Linux**. The goal is a better developer experience: cross-platform automation, simpler happy-path commands, and better visibility into what happened during deploys.

The examples below assume you have an active profile (see [above](#identity-connections--profiles)). Pass `--profile <name>` (or `-p <name>`) to any command to override the active profile for a single invocation.

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
txc env sln import ./Solutions/MySolution_managed.zip -p customer-b-prod
```

**Import a CMT data folder into Dataverse:**
```sh
txc data pkg import ./data-package
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
