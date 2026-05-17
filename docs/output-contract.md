# txc Output Contract

This document defines how every `txc` command communicates results, diagnostics, and errors. All commands **must** follow this contract — deviations cause build failures (via `BannedApiAnalyzers`) or test failures (via `CommandConventionTests`).

## Stream separation

| Stream | Purpose | Who writes |
|--------|---------|------------|
| **stdout** | Command result data (the "answer") | `OutputFormatter` / `OutputWriter` |
| **stderr** | Diagnostic messages: logs, progress, warnings, errors | `ILogger` (via `TxcLoggerFactory`) |
| **exit code** | Machine-readable success/failure signal | Return value of `ExecuteAsync()` |

> **Rule:** Never write diagnostic/log messages to stdout. Never write result data to stderr.

## Output format (`--format`)

Every leaf command inherits a `--format` / `-f` option from `TxcLeafCommand`:

| Value | Behavior |
|-------|----------|
| `json` | JSON output via `TxcOutputJsonOptions.Default` (camelCase, indented, null-safe) |
| `text` | Human-friendly tables, key-value pairs, or plain strings |
| *(omitted)* | **TTY auto-detection:** text when stdout is a terminal, JSON when piped or redirected |

```bash
# Interactive terminal → text table
txc env entity list

# Piped → JSON automatically
txc env entity list | jq '.[] | .logicalName'

# Explicit override
txc env entity list --format json
txc env entity list --format text
```

## Exit codes

| Code | Meaning | When to use |
|------|---------|-------------|
| `0` (`ExitSuccess`) | Operation completed successfully | Default success path |
| `1` (`ExitError`) | Runtime/operational error | Service call failed, network error, unexpected exception |
| `2` (`ExitValidationError`) | Input validation error or resource not found | Bad arguments, missing required input, entity not found |

The base class `TxcLeafCommand.RunAsync()` catches unhandled exceptions and returns `ExitError` (1) automatically. Commands only need explicit exit code handling for validation errors (2).

## Command implementation pattern

Every leaf command **must** extend `TxcLeafCommand` (or `ProfiledCliCommand` for environment-facing commands):

```csharp
[CliCommand(Name = "list", Description = "List widgets.")]
public class WidgetListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(WidgetListCliCommand));

    [CliOption(Name = "--search", Description = "Filter by name.", Required = false)]
    public string? Search { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IWidgetService>();
        var widgets = await service.ListAsync(Profile, Search);

        OutputFormatter.WriteList(widgets, PrintTable);
        return ExitSuccess;
    }

    private static void PrintTable(IReadOnlyList<Widget> items)
    {
        // Text table rendering — only called in text mode
        foreach (var w in items)
            OutputWriter.WriteLine($"  {w.Name,-30} {w.Status}");
    }
}
```

### What the base class provides

- **`--format` / `-f` option** — inherited by all leaf commands
- **`OutputContext` setup** — applies the format flag with TTY auto-detection
- **Standardized try/catch** — catches `ConfigurationResolutionException`, `OperationCanceledException`, and general exceptions
- **`ILogger` requirement** — `protected abstract ILogger Logger` ensures every command has logging
- **Exit code constants** — `ExitSuccess` (0), `ExitError` (1), `ExitValidationError` (2)

### What commands must NOT do

- ❌ Define their own `RunAsync()` — the base class owns it
- ❌ Use `Console.Write*` or `Console.ReadKey` — use `OutputWriter`/`OutputFormatter` and `IHeadlessDetector`
- ❌ Create local `JsonSerializerOptions` — use `TxcOutputJsonOptions.Default`
- ❌ Add `--json` flags — use the inherited `--format` flag instead
- ❌ Catch `ConfigurationResolutionException` — the base class handles it

## Output APIs

| API | When to use |
|-----|-------------|
| `OutputFormatter.WriteData<T>(data, textRenderer?)` | Single object output |
| `OutputFormatter.WriteList<T>(items, tableRenderer?)` | Collection output |
| `OutputFormatter.WriteResult(status, message?, id?)` | Mutative command result envelope |
| `OutputFormatter.WriteValue(key, value)` | Single scalar value |
| `OutputFormatter.WriteDynamicTable(records, tableRenderer)` | Dynamic-schema query results |
| `OutputFormatter.WriteRaw(json, textRenderer?)` | Pre-serialized JSON passthrough |

## JSON envelope for mutative commands

Commands that create, update, or delete resources return a standardized envelope:

```json
{
  "status": "succeeded",
  "message": "Record created successfully.",
  "id": "a1b2c3d4-..."
}
```

In text mode, only the human-readable message is printed.

## Logging

- Use `ILogger` (via `TxcLoggerFactory.CreateLogger(nameof(X))`) for all diagnostic output
- Logs go to **stderr** in all modes:
  - **Terminal mode:** `TxcConsoleFormatter` — `[INFO]`/`[WARN]`/`[ERROR]` prefixes with ANSI color, `HH:mm:ss` timestamps, no category names. Stack traces are suppressed by default; shown only at `Debug` level or with `--verbose`.
  - **Pipe/MCP mode:** structured JSON lines to stderr (`TXC_LOG_FORMAT=json`) — always includes full exception details.
- Log level controlled by `TXC_LOG_LEVEL` env var (default: `Information`)

## MCP integration

The MCP server (`txc-mcp`) spawns `txc` as a subprocess with `TXC_LOG_FORMAT=json` and `TXC_NON_INTERACTIVE=1`:
- **stdout** → captured as the MCP tool result (JSON by default since stdout is redirected)
- **stderr** → parsed as structured JSON log lines, forwarded as MCP log/progress notifications
- **exit code** → determines `isError` in the MCP tool result

Since commands default to JSON when stdout is redirected, the MCP server gets structured data automatically — no `--json` injection needed.

### Structured execution logs

Every MCP tool call produces a retrievable execution log stored in memory. The MCP client can fetch it via the `get_execution_log` tool using the diagnostics URI returned in the tool response.

**Data flow:** Command → `ILogger` → `JsonStderrLogger` → stderr JSON line → `McpLogForwarder` → `RedactedLogEntry` list → `ToolLogStore` → `get_execution_log`

Each log entry preserves its original structure:

| Field | Description |
|-------|-------------|
| `timestamp` | ISO-8601 UTC timestamp |
| `level` | Log level: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` |
| `category` | Logger category (typically `nameof(CommandClass)`) |
| `message` | Redacted log message |
| `data` | Structured data dictionary from message template placeholders (nullable) |

The `get_execution_log` tool supports filtering and pagination:
- `level` — minimum level filter (e.g., `"Error"` returns only Error + Critical)
- `category` — filter by category substring
- `search` — full-text search across messages and data values
- `skip` / `take` — pagination (defaults: skip=0, take=50)

### Progress streaming

Long-running commands should emit frequent `ILogger` messages so MCP clients see real-time progress. `McpLogForwarder` converts each stderr log line into an MCP `notifications/progress` notification (rate-limited to 500ms). If a log entry includes a `Progress` integer property, that semantic value is used instead of the raw line count.

**Good pattern for progress:**
```csharp
Logger.LogInformation("Importing solution {Current}/{Total}: {SolutionName}",
    current, total, name);
```

### Structured logging requirement

All `ILogger` calls must use **message templates**, not string interpolation. This ensures the `data` dictionary in structured log entries is populated with named values.

```csharp
// ✅ Correct — populates data: { "Entity": "account", "Error": "..." }
Logger.LogError("Failed to process {Entity}: {Error}", entityName, ex.Message);

// ❌ Wrong — data dict is empty, structured filtering loses information
Logger.LogError($"Failed to process {entityName}: {ex.Message}");
```

## Enforcement

| Mechanism | What it catches | When |
|-----------|----------------|------|
| `BannedApiAnalyzers` (RS0030) | `Console.Write*`, `Console.ReadKey`, `new HttpClient()`, `new JsonSerializerOptions()`, `Thread.Sleep`, `Task.Result`/`.GetAwaiter().GetResult()`, `throw new Exception()`, `Newtonsoft.Json` | Build time (error) |
| `TxcLeafCommand` abstract members | Missing `Logger`, missing `ExecuteAsync()` | Build time (error) |
| `TXC001` (Roslyn analyzer) | Leaf `[CliCommand]` not inheriting `TxcLeafCommand` | Build time (error) |
| `TXC002` (Roslyn analyzer) | Leaf command defining own `RunAsync()` | Build time (error) |
| `TXC003` (Roslyn analyzer) | Direct `OutputWriter` calls in command code (auto-suppresses text-renderer lambdas) | Build time (error) |
| `TXC014` (Roslyn analyzer) | String interpolation in `ILogger` calls (must use message templates) | Build time (warning) |
| `CommandConventionTests` | Non-conforming commands, stale `--json` flags, local `JsonSerializerOptions` | Test time |
| `LayeringTests` | Feature→Feature project references, `--yes` commands missing `[McpIgnore]` | Test time |

## Adding a new command

1. Create a class with `[CliCommand]` extending `TxcLeafCommand` (or `ProfiledCliCommand`)
2. Implement `protected override ILogger Logger { get; }` and `protected override Task<int> ExecuteAsync()`
3. Use `OutputFormatter` for all output
4. Use message templates (not string interpolation) in all `ILogger` calls
5. For long-running operations, emit periodic `Logger.LogInformation` messages for progress visibility
6. Return `ExitSuccess`, `ExitError`, or `ExitValidationError`
7. Run tests to verify convention compliance
