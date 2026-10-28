# Deployment Workflow

## The Full Pipeline

```
local dev → build → pack → import → publish
```

### 1. Local Development
`txc workspace component create <type> --output <project-dir> [--param <name>=<value> ...]` scaffolds components (see [component-creation](component-creation.md)). All local, in source control.

### 2. Build
Build the solution project locally to catch malformed XML, missing references and schema violations early.

### 3. Pack
```
txc environment solution pack <unpacked-solution-folder> --output <solution.zip> [--managed]
```
Local only; needed only for a plain unpacked folder (a Build SDK solution project's build already produces the package).

### 4. Import
```
txc environment solution import <solution.zip|unpacked-folder|project-dir> [--wait]
```
The path defaults to the current directory. For a Build SDK project directory, import runs `dotnet build` itself and imports the output zip, so no separate build is needed. By default it returns immediately with an `asyncOperationId`.

`--wait` blocks until the import finishes (can take minutes). **If the caller has a timeout** (an MCP request, a short shell timeout), **do not use `--wait`**: poll `txc environment deployment get --async-operation-id <guid>` (or `--latest`) until status is `Completed` or `Failed`, then review its findings (warnings, errors). Where nothing times out (a CI job), `--wait` is fine. Never query the `asyncoperation` table directly; `deployment get` parses the findings.

### 5. Publish
```
txc environment solution publish
```
Without it, changes to forms, views and other UI components are not visible to users.

## Managed vs Unmanaged

For a Build SDK solution project, `Debug` builds unmanaged (dev/test) and any other configuration (e.g. `Release`) managed (staging/production); the project property `SolutionPackageType` (`Unmanaged` or `Managed`) overrides that, so check the project. `txc environment solution pack` produces unmanaged unless `--managed`.

An unmanaged solution cannot be overwritten by a managed one (or vice versa); the import fails with "You can't overwrite an unmanaged version of this solution with a managed version." Before deploying:
1. Check what the environment holds: `txc environment solution list --managed true` / `--managed false`, or `txc environment solution get <solution-name>`.
2. Build with the matching configuration; on a mismatch, uninstall the existing solution and re-import, or rebuild.

## Pre-Flight Checks

| Check | Command |
|---|---|
| Auth/connection is valid | `txc config profile validate [<name>]` |
| Correct environment | `txc config profile get [<name>]` |
| Solution can be safely removed | `txc environment solution uninstall-check <solution-name>` |

## Changeset Workflow

Mutating record, table, column, relationship and option-set commands take `--apply` (run now) or `--stage` (add to a local changeset). Staged operations live in `.txc/changeset.json` of the current directory, so stage and apply from the same directory.

| Command | Purpose |
|---|---|
| `txc environment changeset status` | Show staged operations |
| `txc environment changeset apply --strategy batch\|transaction\|bulk` | Run them (`--continue-on-error` applies to `batch`) |
| `txc environment changeset discard` | Drop them (`--yes` skips the confirmation) |

If import fails, read `deployment get` first: see Deployment Failures in [troubleshooting](troubleshooting.md).

See also: [troubleshooting](troubleshooting.md), [solution-layering](solution-layering.md)
