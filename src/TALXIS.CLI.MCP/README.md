# TALXIS CLI MCP (`txc-mcp`)

This project provides a Model Context Protocol (MCP) server for the TALXIS CLI, enabling dynamic discovery and invocation of CLI tools via the MCP stdio transport. The MCP server is distributed as a global .NET tool and can be easily integrated with GitHub Copilot and other MCP-compatible tools.

## Installation

Install the MCP server as a global .NET tool (this will add the `txc-mcp` alias):

```sh
dotnet tool install --global TALXIS.CLI.MCP
```

See the package on NuGet: [TALXIS.CLI.MCP](https://www.nuget.org/packages/TALXIS.CLI.MCP)

## Usage with VS Code and GitHub Copilot

To use the MCP server in VS Code (or any MCP-compatible tool), create a `.vscode/mcp.json` file in your project with the following content:

```json
{
    "inputs": [],
    "servers": {
        "TALXIS CLI Public": {
            "type": "stdio",
            "command": "txc-mcp"
        }
    }
}
```

This will allow GitHub Copilot and other tools to discover and invoke TALXIS CLI commands via MCP.

## Developing and Debugging Locally

When developing the MCP server locally, you can run it directly from source and configure VS Code to use your local build. In your `.vscode/mcp.json`, set the server command to launch the project via `dotnet run`:

```json
{
    "inputs": [],
    "servers": {
        "TALXIS CLI Dev": {
            "type": "stdio",
            "command": "dotnet",
            "args": [
                "run",
                "--project",
                "${workspaceFolder}/src/TALXIS.CLI.MCP/TALXIS.CLI.MCP.csproj"
            ]
        }
    }
}
```

- Adjust the path in `args` to match your local project location if needed.
- This setup allows you to test changes without reinstalling the global tool.

You can also use the [Model Context Protocol Inspector](https://www.npmjs.com/package/@modelcontextprotocol/inspector) for interactive inspection:

```sh
npx @modelcontextprotocol/inspector dotnet run --project src/TALXIS.CLI.MCP
```

## Features
- Dynamic discovery of CLI commands and subcommands using reflection
- Implements MCP ListTools and CallTool handlers

---

For more details, see the main [TALXIS CLI README](../../README.md).