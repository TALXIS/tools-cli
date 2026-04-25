# Proposed Roslyn Rules & Code Protections

Based on bugs discovered during the CMT enhancements PR, these rules would prevent recurrence.

## Immediate Actions (config-only, no custom code)

### 1. Nullable warnings → errors (`.editorconfig`)
Prevents `null!` returns on non-nullable methods.
```ini
dotnet_diagnostic.CS8603.severity = error   # Possible null reference return
```

### 2. Namespace-folder mismatch (`.editorconfig`)
Prevents types ending up in wrong namespaces.
```ini
dotnet_diagnostic.IDE0130.severity = warning
```

### 3. TXC004 in WarningsAsErrors (`Directory.Build.props`)
Ensures safety annotations can't be skipped.

## Proposed Custom Analyzers

| Rule | Title | Prevents |
|------|-------|----------|
| TXC005 | No raw integer returns in ExecuteAsync | `return 0` instead of `ExitSuccess` |
| TXC006 | No try-catch in ExecuteAsync (base class handles it) | Duplicate error handling |
| TXC007 | No `--json` CLI option (use base `--format`) | Redundant parameters |
| TXC008 | Must override Logger property, not shadow with field | Broken base class logging |
| TXC009 | Public enum members must have explicit values | Binary compatibility breaks |

## Coverage

| Protection | Bugs Caught |
|-----------|-------------|
| Existing (RS0030, TXC002-004) | 5 of 13 |
| Proposed config changes | +3 |
| Proposed custom analyzers | +5 |
| **Total** | **13 of 13** |
