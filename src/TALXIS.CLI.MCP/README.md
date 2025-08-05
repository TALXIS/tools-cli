# TALXIS CLI MCP (`txc-mcp`)

This project provides a Model Context Protocol (MCP) server for the TALXIS CLI, enabling dynamic discovery and invocation of CLI tools via the MCP stdio transport. The MCP server is distributed as a global .NET tool and can be easily integrated with GitHub Copilot and other MCP-compatible tools.

## Installation

**Install the MCP server as a global .NET tool (this will add the `txc-mcp` alias):**

```sh
dotnet tool install --global TALXIS.CLI.MCP
```

**Update to the latest version:**

```sh
dotnet tool update --global TALXIS.CLI.MCP
```

See the package on NuGet: [TALXIS.CLI.MCP](https://www.nuget.org/packages/TALXIS.CLI.MCP)

## Usage with VS Code and GitHub Copilot

### Quick Installation via Deep Link

Click the link below to automatically add the TALXIS CLI MCP server to your VS Code configuration:

[📦 Install TALXIS CLI MCP Server](vscode:mcp/install?%7B%22name%22%3A%22TALXIS%20CLI%22%2C%22command%22%3A%22txc-mcp%22%7D)

This will add the server to your user configuration, making it available across all workspaces.

### Manual Configuration

Alternatively, you can manually create a `.vscode/mcp.json` file in your project with the following content:

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

## Testing and Debugging

### Interactive Manual Testing

You can use the [Model Context Protocol Inspector](https://www.npmjs.com/package/@modelcontextprotocol/inspector) for interactive inspection:

```sh
npx @modelcontextprotocol/inspector dotnet run --project src/TALXIS.CLI.MCP
```

> **Note:** The Inspector is an interactive web browser application designed for manual testing and exploration. It is not suitable for automated testing scenarios.

### Command Line Debugging & Automated Testing

For debugging or automated testing, you can interact with the MCP server using JSON-RPC messages over stdin/stdout:

```sh
# Start the server
dotnet run --project src/TALXIS.CLI.MCP

# Then send JSON-RPC messages via stdin (one per line):
# 1. Initialize the connection (required by MCP protocol)
{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "test-client", "version": "1.0.0"}}}

# 2. List available tools
{"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}}

# 3. Call a specific tool (example)
{"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "tool-name", "arguments": {}}}
```


#### Example JSON-RPC Messages

To list available tools:

```json
{"jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {}}
```

To call a tool (replace arguments as needed):

```json
{"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": "workspace_component_create", "arguments": {"ShortName": "pp-entity", "name": "TestEntity", "Param": ["EntityType=InvalidType"]}}}
```

---

For more details, see the main [TALXIS CLI README](../../README.md).