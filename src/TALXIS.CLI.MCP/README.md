# TALXIS.CLI.MCP

This project hosts a ModelContextProtocol (MCP) server for the TALXIS CLI, advertising a dynamic list of CLI tools and allowing tool invocation via MCP stdio transport.

## Features
- Dynamic discovery of CLI commands and subcommands using reflection
- Implements MCP ListTools and CallTool handlers  

## Debugging and Testing Locally

You can debug and test the MCP server locally in two main ways:

### 1. Using the Model Context Protocol Inspector

You can use the [Model Context Protocol Inspector](https://www.npmjs.com/package/@modelcontextprotocol/inspector) for interactive inspection and debugging:

```sh
npx @modelcontextprotocol/inspector dotnet run --project src/TALXIS.CLI.MCP
```

This will launch the MCP Inspector and connect it to the running server.

### 2. Using VS Code with `.vscode/mcp.json`

You can also test the MCP server integration with VS Code by opening another window and adding a `.vscode/mcp.json` file to your project with the following content:

```json
{
    "inputs": [],
    "servers": {
        "TALXIS CLI": {
            "type": "stdio",
            "command": "dotnet",
            "args": [
                "run",
                "--project",
                "/Users/tomasprokop/Desktop/Repos/tools-cli/src/TALXIS.CLI.MCP/TALXIS.CLI.MCP.csproj"
            ]
        }
    }
}
```

**Steps:**

1. Open a new VS Code window in any folder (it does not have to be this repo).
2. Create a `.vscode` directory if it does not exist.
3. Add the above `mcp` file as `.vscode/mcp`.
4. Make sure the path in `args` points to the correct location of your `TALXIS.CLI.MCP.csproj` file.