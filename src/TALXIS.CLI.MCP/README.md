# TALXIS.CLI.MCP

This project hosts a ModelContextProtocol (MCP) server for the TALXIS CLI, advertising a dynamic list of CLI tools and allowing tool invocation via MCP stdio transport.

## Features
- Dynamic discovery of CLI commands and subcommands using reflection
- Implements MCP ListTools and CallTool handlers

## Usage

Build and run the server:

```sh
dotnet run --project src/TALXIS.CLI.MCP
```

The server will listen for MCP stdio requests and advertise available CLI tools.
  

## Debugging Locally

You can debug the MCP server locally using the [Model Context Protocol Inspector](https://www.npmjs.com/package/@modelcontextprotocol/inspector):

```sh
npx @modelcontextprotocol/inspector dotnet run --project src/TALXIS.CLI.MCP
```

This will launch the MCP Inspector and connect it to the running server for interactive inspection and debugging.