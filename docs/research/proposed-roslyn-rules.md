# Roslyn Rules & Code Protections

> **Status:** All rules described below are **implemented** and enforced as build errors via `WarningsAsErrors` in `src/Directory.Build.props`. Custom analyzers live in `src/TALXIS.CLI.Analyzers/`.

Based on bugs discovered during the CMT enhancements PR, these rules prevent recurrence.

## Config-level protections (`.editorconfig`)

### 1. Nullable warnings (`.editorconfig`)
Prevents `null!` returns on non-nullable methods.
```ini
# src/.editorconfig — currently set to warning (not error)
dotnet_diagnostic.CS8600.severity = warning
dotnet_diagnostic.CS8601.severity = warning
dotnet_diagnostic.CS8602.severity = warning
dotnet_diagnostic.CS8603.severity = warning
dotnet_diagnostic.CS8604.severity = warning
dotnet_diagnostic.CS8625.severity = warning
```

> **Note:** These are set to `warning` in `.editorconfig`, not `error`. The original proposal suggested `error`, but the current configuration uses `warning` severity.

### 2. Namespace-folder mismatch (`.editorconfig`)
Prevents types ending up in wrong namespaces.
```ini
# src/.editorconfig — currently set to warning
dotnet_diagnostic.IDE0130.severity = warning
```

### 3. All TXC rules in WarningsAsErrors (`Directory.Build.props`)
All custom analyzer rules are enforced as errors:
```xml
<WarningsAsErrors>RS0030;TXC001;TXC002;TXC003;TXC004;TXC005;TXC006;TXC007;TXC008;TXC009</WarningsAsErrors>
```

## Custom Analyzer Rules

All rules are implemented in `src/TALXIS.CLI.Analyzers/` and enforced as build errors.

| Rule | Title | Prevents |
|------|-------|----------|
| TXC001 | Leaf command must inherit `TxcLeafCommand` | Commands missing base class behaviour (output, logging, exit codes) |
| TXC002 | Must not define `RunAsync()` | Commands overriding the wrong entry point instead of `ExecuteAsync()` |
| TXC003 | Must not call `OutputWriter` directly | Bypassing `OutputFormatter` — breaks `--format` contract |
| TXC004 | Must declare safety annotation | Missing `[CliDestructive]`, `[CliReadOnly]`, or `[CliIdempotent]`; destructive commands must implement `IDestructiveCommand` |
| TXC005 | No raw integer returns in `ExecuteAsync` | `return 0` instead of `ExitSuccess` |
| TXC006 | No try-catch in `ExecuteAsync` (base class handles it) | Duplicate error handling |
| TXC007 | No `--json` CLI option (use base `--format`) | Redundant parameters |
| TXC008 | Must override `Logger` property, not shadow with field | Broken base class logging |
| TXC009 | Public enum members must have explicit values | Binary compatibility breaks |

## Coverage

| Protection | Bugs Caught |
|-----------|-------------|
| Existing (RS0030, TXC001-004) | 5 of 13 |
| Config changes (nullable, namespace) | +3 |
| Custom analyzers (TXC005-009) | +5 |
| **Total** | **13 of 13** |
