# Output & Error Handling Review — Can We Write Errors to stdout?

## 1. Current Behavior (What Happens on Error Today)

### Error flow in `TxcLeafCommand.RunAsync()`

When `ExecuteAsync()` throws, the base class catch block:
1. Logs the error message via `Logger.LogError(...)` → goes to **stderr** (via `ILogger` / `TxcLoggerFactory`)
2. Logs the full stack trace at `Debug` level → also **stderr**
3. Returns exit code `1` (runtime error) or `2` (validation error)

**Nothing is written to stdout on failure.** The command's stdout is empty unless `ExecuteAsync()` wrote something before throwing.

### What the MCP adapter sees

`CliSubprocessRunner` spawns `txc` as a subprocess with `RedirectStandardOutput = true` and `RedirectStandardError = true`, plus `TXC_LOG_FORMAT=json`.

- **stdout** → captured line-by-line via `McpLogForwarder.OnStdoutLineAsync()` → accumulated in `_stdoutBuffer` → becomes `StdoutContent` → used as `result.Output`
- **stderr** → captured line-by-line via `McpLogForwarder.OnStderrLineAsync()` → parsed as JSON log lines → forwarded as MCP `notifications/message` to the client; Error/Critical messages accumulated in `_errorMessages` → becomes `LastErrors`
- **exit code** → determines `IsError` in the `CallToolResult`

### `BuildToolResult()` logic (Program.cs lines 355-391)

```
if (exitCode == 0):
    content = [TextContentBlock(result.Output)]        // stdout
else:
    content = [TextContentBlock(result.LastErrors)]     // stderr error lines
    + optional ResourceLinkBlock to full log
    IsError = true
```

**Key insight:** On failure, `result.Output` (stdout) is **discarded**. The MCP server uses `result.LastErrors` (extracted from stderr) as the error text returned to the AI agent. Any stdout content from a failed command is lost.

## 2. Is There a Pattern for Writing Errors to stdout?

**No.** No existing command writes error information to stdout. The pattern is:
- Success → `OutputFormatter.WriteResult("succeeded", ...)` or `OutputFormatter.WriteData(...)` to stdout
- Failure → `Logger.LogError(...)` to stderr, return non-zero exit code

One command uses `OutputFormatter.WriteResult("failed", ...)` for a **partial success** scenario (`ComponentCreateCliCommand` — scaffolding succeeded but post-actions failed), but this still returns `ExitError`.

## 3. What Does the Output Contract Say About Errors?

The output contract (`docs/output-contract.md`) defines:

- **Stream separation rule:** "Never write diagnostic/log messages to stdout. Never write result data to stderr."
- **Exit codes** are the machine-readable success/failure signal
- **`OutputFormatter.WriteResult(status, message, id?)`** is the envelope for mutative commands — the `status` field can be `"succeeded"` or `"failed"`
- There is **no dedicated error envelope** (e.g., `{ "error": "...", "exitCode": 1 }`)

The existing `CommandResultEnvelope` (`{ status, message, id }`) could carry `status: "failed"`, but this isn't used as a general error reporting mechanism.

## 4. Can `TxcLeafCommand`'s Catch Block Write to stdout?

### Technically: Yes, safely

Adding an `OutputFormatter.WriteResult("failed", errorMessage)` call in the catch blocks **would not break the MCP adapter**, because:

1. The MCP subprocess captures stdout via `McpLogForwarder._stdoutBuffer` — all stdout lines are accumulated regardless of exit code
2. **However**, `BuildToolResult()` currently ignores `result.Output` on failure — it only uses `result.LastErrors` (from stderr)

So writing to stdout on error would be **safe but invisible to MCP** unless `BuildToolResult()` is also updated.

### What would need to change for MCP to benefit:

```
// Current (failure path):
content = [TextContentBlock(result.LastErrors)]

// Proposed (failure path — prefer stdout if available):
content = [TextContentBlock(result.Output.IsNotEmpty ? result.Output : result.LastErrors)]
```

Or combine both: stdout error envelope + stderr diagnostic details.

### TXC003 analyzer impact:

The `MustNotCallOutputWriterAnalyzer` (TXC003) flags `OutputWriter.Write/WriteLine` calls inside `[CliCommand]` classes. But `TxcLeafCommand` itself is not a `[CliCommand]` class — it's the abstract base. The analyzer only checks types with `[CliCommandAttribute]`, so adding `OutputFormatter.WriteResult()` in `TxcLeafCommand.RunAsync()` **would not trigger TXC003**.

### TXC006 impact:

The `NoTryCatchInExecuteAsync` analyzer (TXC006) prevents try-catch in `ExecuteAsync()` — it does not affect the base class `RunAsync()`.

## 5. What Does `OutputContext.IsJson` Do in the Error Path?

`OutputContext.Format` is set by `ApplyOutputFormat()` at the top of `RunAsync()`, **before** the catch blocks execute. So `OutputContext.IsJson` is available and correct in the error path.

This means:
- **JSON mode** (pipes, MCP): `OutputFormatter.WriteResult("failed", message)` → `{ "status": "failed", "message": "..." }`
- **Text mode** (terminal): `OutputFormatter.WriteResult("failed", message)` → plain text message

This is exactly the right behavior — MCP gets a structured JSON error, humans get a readable message.

## 6. Does `OutputFormatter` Have a `WriteError` Method?

**No.** There is no `WriteError` method. The closest is:
- `WriteResult(status, message, id?)` — used by mutative commands with `status: "succeeded"` or `"failed"`
- There's no `WriteError()` or general error-reporting method

### Options:

**Option A — Reuse `WriteResult`:**
```csharp
OutputFormatter.WriteResult("failed", ex.Message);
```
This uses the existing `CommandResultEnvelope` (`{ status, message, id }`). Simple, no new API.

**Option B — Add `WriteError`:**
```csharp
public static void WriteError(string message, int exitCode) { ... }
```
With a dedicated `ErrorEnvelope`: `{ "error": true, "message": "...", "exitCode": 1 }`. More explicit, but adds API surface.

**Recommendation:** Option A (`WriteResult("failed", ...)`) is simpler and consistent with how `ComponentCreateCliCommand` already uses it.

## 7. TXC003 Rules Summary

**TXC003 (`MustNotCallOutputWriterAnalyzer`):**
- Fires on `OutputWriter.Write()` or `OutputWriter.WriteLine()` calls inside `[CliCommand]`-attributed classes
- **Suppressed** inside lambda/anonymous-method text-renderers passed to `OutputFormatter.WriteData`, `WriteList`, `WriteRaw`, `WriteDynamicTable`
- Named method-group renderers (e.g., `PrintTable`) are NOT auto-suppressed — need `#pragma warning disable TXC003`
- Does NOT fire inside `OutputFormatter` itself or inside `TxcLeafCommand` (which has no `[CliCommand]` attribute)

**Bottom line:** Adding `OutputFormatter.WriteResult()` in `TxcLeafCommand`'s catch block is fully compliant with TXC003.

---

## Recommended Approach

### What to do:

1. **In `TxcLeafCommand.RunAsync()` catch blocks:** Add `OutputFormatter.WriteResult("failed", errorMessage)` before the `return ExitError` / `return ExitValidationError`. This writes a structured error to stdout (JSON envelope in pipe/MCP mode, plain text in terminal mode).

2. **In `BuildToolResult()` (MCP Program.cs):** On failure, prefer `result.Output` (stdout) if non-empty, fall back to `result.LastErrors` (stderr). This way the MCP client gets the structured error envelope.

3. **No new `OutputFormatter` methods needed:** `WriteResult("failed", ...)` already exists and fits.

### What this gives us:

| Consumer | Before | After |
|----------|--------|-------|
| **Terminal user** | Error only in stderr log lines | Error in stderr **and** a clean message on stdout |
| **Pipe / jq** | Empty stdout + non-zero exit code | `{ "status": "failed", "message": "..." }` on stdout |
| **MCP AI agent** | Error from stderr log parsing | Structured JSON error envelope from stdout |

### What it does NOT break:

- ✅ Stream separation: errors are still logged to stderr (diagnostic). The stdout message is "result data" (the command's answer is "I failed because X").
- ✅ TXC003 analyzer: `TxcLeafCommand` has no `[CliCommand]` attribute
- ✅ Exit codes: unchanged (still 1 or 2)
- ✅ Existing commands: no changes needed — they already use `OutputFormatter.WriteResult("succeeded", ...)` on success
- ✅ MCP log forwarding: stderr logs still flow as MCP notifications

### Should this go in this PR or a follow-up?

**Follow-up PR.** Reasons:
- It's a cross-cutting behavior change (affects every command's error path)
- Needs corresponding changes in `BuildToolResult()` on the MCP side
- Should have test coverage (verify the JSON envelope shape, verify MCP picks it up)
- The current PR's scope should remain focused

The follow-up is small (≈20 lines across 2 files + tests) and self-contained.
