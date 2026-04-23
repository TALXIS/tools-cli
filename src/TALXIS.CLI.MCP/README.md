# TALXIS CLI MCP (`txc-mcp`)

This project provides a Model Context Protocol (MCP) server for the TALXIS CLI, enabling dynamic discovery and invocation of CLI tools via the MCP stdio transport. The MCP server is distributed as a global .NET tool and can be easily integrated with GitHub Copilot and other MCP-compatible tools.

## Features

- **Tool Discovery & Execution** — Dynamically discovers and exposes all TALXIS CLI commands as MCP tools with typed input schemas
- **Task-Augmented Execution** — Long-running tools (deploy, import) support the MCP tasks protocol for async "call-now, fetch-later" execution with cancellation support
- **Structured Logging** — Streams real-time log output from CLI subprocesses to MCP clients via `notifications/message`
- **Progress Notifications** — Emits `notifications/progress` during long-running tool calls when the client provides a `progressToken`, with rate-limiting to avoid flooding
- **Workspace Roots** — Requests the client's workspace roots via `roots/list` and uses the primary root as the subprocess working directory
- **Log Redaction** — Automatically redacts passwords, tokens, and filesystem paths from log output before forwarding to clients
- **Copilot Instructions** — Manages `.github/copilot-instructions.md` files to guide AI assistants working with TALXIS repositories

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

# 4. Call a tool with progress tracking
{"jsonrpc": "2.0", "id": 4, "method": "tools/call", "params": {"name": "environment_deploy", "arguments": {"Package": "MyPackage"}, "_meta": {"progressToken": "my-progress-1"}}}

# 5. Call a long-running tool as a task (returns immediately with task ID)
{"jsonrpc": "2.0", "id": 5, "method": "tools/call", "params": {"name": "environment_deploy", "arguments": {"Package": "MyPackage"}, "task": {}}}

# 6. Poll task status
{"jsonrpc": "2.0", "id": 6, "method": "tasks/get", "params": {"taskId": "<task-id-from-step-5>"}}

# 7. Get task result (blocks until complete)
{"jsonrpc": "2.0", "id": 7, "method": "tasks/result", "params": {"taskId": "<task-id-from-step-5>"}}
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

## Auth contract (stdio)

**The MCP server never runs an interactive auth flow.** Every `txc` tool
subprocess spawned by this server is forced headless — stdout is reserved
for JSON-RPC frames and the child has stdin/stdout redirected, so a
browser/device-code/masked-prompt attempt would either hang the session
or corrupt the transport. `CliSubprocessRunner` sets
`TXC_NON_INTERACTIVE=1` unconditionally on every spawn.

Prerequisites, before invoking any tool that touches a Dataverse
environment (or any other Connection-bound tool):

1. On the human's machine, run `txc config auth login` (interactive
   browser) or `txc config auth add-service-principal` once to register
   a credential and prime the MSAL token cache.
2. Run `txc config connection create <name> --provider dataverse ...`
   to register the endpoint.
3. Run `txc config profile create <name> --auth <alias> --connection <name>`
   to bind them, then `txc config profile select <name>` (or pin the
   workspace via `txc config profile pin`).

After that, MCP tool calls resolve the active profile silently via the
acquired token cache or stored SPN secret. If resolution fails (expired
refresh token, missing credential, broken config), the subprocess exits
non-zero and the MCP server surfaces the error through the tool-call
result; the structured log line includes the fail-fast remedy string
(`txc config profile validate <name>`).

### Per-call profile override

Any Connection-touching MCP tool accepts an optional `profile` argument
which is forwarded to the child as `--profile <name>`. This lets a
single MCP session switch between profiles per call without restarting
the server — e.g. an agent can target `customer-a-dev` for one tool and
`customer-b-prod` for the next.

### Client env allow-list (`mcpServers.<name>.env`)

The MCP server inherits the client's environment and the child CLI
subprocess inherits that. The following are meaningful to `txc`; anything
else is ignored:

| Variable | Purpose |
|---|---|
| `TXC_PROFILE` | Select active profile for every call in this MCP session (overridden by per-call `profile` argument when supplied). |
| `TXC_CONFIG_DIR` | Override entire config directory (total CI isolation). |
| `TXC_NON_INTERACTIVE` | Redundant here — MCP already forces this to `1`. |
| `TXC_LOG_FORMAT` | Redundant here — MCP already forces `json`. |
| `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID` | Service-principal fallback for CI / headless runners. |
| `AZURE_FEDERATED_TOKEN_FILE` | Kubernetes / Azure workload-identity federation. |
| `ACTIONS_ID_TOKEN_REQUEST_URL`, `ACTIONS_ID_TOKEN_REQUEST_TOKEN` | GitHub Actions OIDC federation. |
| `TXC_ADO_ID_TOKEN_REQUEST_URL`, `TXC_ADO_ID_TOKEN_REQUEST_TOKEN` | Azure DevOps pipelines workload-identity federation (legacy `PAC_ADO_*` also honored). |

Secrets (client secrets, PATs, certificate passwords) are **never**
accepted as plain MCP tool arguments — they are stored in the OS-level
secret vault via `txc config auth add-service-principal` and referenced
from config by `SecretRef` handle only.

### Log redaction

`McpLogForwarder` runs every child stderr JSON log line through
`LogRedactionFilter` before emitting `notifications/message` to the MCP
client. Patterns covered: `Bearer <token>`, `Authorization:` headers,
bare JWTs, connection-string secret keys (`Password`, `ClientSecret`,
`AccessToken`, `RefreshToken`, etc.) and URL query-param secrets
(`code=`, `token=`, `access_token=`). This is the last-chance belt for
accidental `logger.LogError(ex, ...)` leaks where the Dataverse or
Microsoft.Identity SDKs embed tokens inside exception messages.

### Forbidden patterns (forward-compat for HTTP transport)

When the HTTP/SSE transport ships, `txc-mcp` will be an **OAuth resource
server only** — Entra ID remains the authorization server. The following
patterns are forbidden by the MCP spec and will not be implemented:

- **Token passthrough**: never forward a client's bearer token directly
  to Dataverse — always use on-behalf-of or a separate service account.
- **Tokens in URIs**: never embed access tokens in query strings.
- **Non-HTTPS redirects** (except loopback for local dev).
- **Missing PKCE**: all OAuth flows must use PKCE.
- **Missing audience check**: tokens must be audience-bound to the
  `txc-mcp` resource URI (RFC 8707).

See `docs/mcp-http-auth-notes.md` (design notes, no code in v1).

---

For more details, see the main [TALXIS CLI README](../../README.md).
