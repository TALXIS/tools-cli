# TALXIS CLI MCP Integration Tests

This project contains integration tests for the TALXIS CLI MCP server. It uses xUnit and launches the MCP server as a subprocess, sending JSON-RPC messages to validate behavior.

## Running the Tests

```sh
cd tests/TALXIS.CLI.MCP.Tests
 dotnet test
```

## What is Tested
- MCP server startup and initialization
- Listing available tools
- (Extend with more tests for error handling, template validation, etc.)

## Adding More Tests
Add more `[Fact]` or `[Theory]` methods to `McpServerIntegrationTests.cs` to cover additional scenarios, such as:
- Invalid template names
- Missing/invalid parameters
- Error message propagation
- Valid/invalid tool invocations

---

For more details, see the main [TALXIS CLI README](../../README.md).
