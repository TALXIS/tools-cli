---
# TALXIS CLI (txc)

> [!WARNING]
> This project is currently in a development phase and not ready for production use.
> While we actively use these tools internally, our aim is to share and collaborate with the broader community to refine and enhance their capabilities.
> We are in the process of gradually open-sourcing the code, removing internal dependencies to make it universally applicable.
> At this stage, it serves as a source of inspiration and a basis for collaboration.
> We welcome feedback, suggestions, and contributions through pull requests.

If wish to use this project for your team, please contact us at hello@networg.com for a personalized onboarding experience and customization to meet your specific needs.

## Goal

The goal of the TALXIS CLI (`txc`) is to provide a modular, extensible .NET global tool that helps Power Platform developers automate tasks across their local code repositories and data manipulation.

## Command Groups

The CLI is organized into modular command groups. Each group provides a set of related commands.

### Data Commands (`txc data`)

Data-related utilities for ETL, Power Query, and automation scenarios.

#### `server` command

Starts a simple local HTTP server exposing endpoints for ETL/data transformation tasks. Useful for integrating with Power Query or other local ETL tools.

**Usage:**

```sh
txc data server [--port <port>]
```

- `--port` (optional): Port to run the server on. Defaults to `50505` if not specified.

**Example:**

```sh
txc data server --port 50505
```

**Endpoints:**


- `POST /ComputePrimaryKey` — Accepts a JSON body `{ "table": "your-table-name", "id": "your-id-string" }` (case-insensitive) and returns a deterministic GUID as `{ "primaryKey": "..." }`. The `table` parameter is used as a prefix to avoid collisions between tables.

**Sample request:**

```sh
curl -X POST http://localhost:50505/ComputePrimaryKey \
  -H "Content-Type: application/json" \
  -d '{"table":"talxis_opportunity","id":"myidentifiers"}'
```



## Collaboration
We are happy to collaborate with developers and contributors interested in enhancing Power Platform development processes. If you have feedback, suggestions, or would like to contribute, please feel free to submit issues or pull requests.

## Local building and debugging

To build and debug the CLI locally:

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
3. Run the CLI directly (for example, to test the data server):
   ```sh
   dotnet run --project src/TALXIS.CLI -- data server
   ```
4. You can also debug using Visual Studio or VS Code by opening the solution and setting breakpoints as needed.

## Releasing a New Version

This project uses explicit Microsoft-style versioning (e.g., 1.0.0.0) set in the `Directory.Build.props` file. A GitHub Actions workflow publishes new releases to NuGet.org.
