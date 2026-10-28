# Deployment Workflow

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## The Full Pipeline

```
local dev → build → pack → import → publish → verify
```

Each step has specific `txc` commands and checkpoints.

## Step-by-Step

### 1. Local Development
Use `txc workspace component create <type> --output <project-dir> [--param <name>=<value> ...]` to scaffold components (see [component-creation](component-creation.md)). Edit XML files. Write plugin code. All local, all in source control.

### 2. Build
Build the solution project locally to catch errors early — malformed XML, missing references, schema violations.

### 3. Pack
```
txc environment solution pack <unpacked-solution-folder> --output <solution.zip> [--managed]
```
Creates a `.zip` solution file from an **unpacked solution folder** (add `--managed` for a managed package). This is still a local operation — no environment interaction yet. For a TALXIS Build SDK solution project the build in step 2 (see Build Configuration below) already produces the package, and `txc environment solution import` accepts the project directory or the built `.zip` directly, so this step is only needed for a plain unpacked folder.

### 4. Import
```
txc environment solution import <solution.zip|unpacked-folder|project-dir> [--wait]
```
Uploads the solution package to the target Dataverse environment (the path defaults to the current directory). By default it returns immediately with an `asyncOperationId`.

Solution imports can take minutes, and `--wait` blocks until they finish. **If the caller has a timeout** (an MCP request, a short shell timeout, a tool that gives up after some minutes), **do not use `--wait`**: start the import, then poll `txc environment deployment get --async-operation-id <guid>` (the id is printed by `txc environment solution import`) until status is `Completed` or `Failed`. Where nothing times out (a CI job, a terminal you can leave running), `--wait` is fine and simpler. Never query the `asyncoperation` table directly via SQL — `txc environment deployment get` parses the findings for you.

### 5. Publish
```
txc environment solution publish
```
Publishes all customizations. Without this step, changes to forms, views, and other UI components won't be visible to users.

### 6. Verify
```
txc environment deployment get --latest
```
Check the deployment status and review any findings (warnings, errors, informational messages).

## Build Configuration: Managed vs Unmanaged

For a TALXIS Build SDK solution project the build configuration decides whether the package is **managed** or **unmanaged**, and getting it wrong is a common deployment failure. By default `Debug` builds unmanaged and any other configuration (such as `Release`) builds managed; the project property `SolutionPackageType` (`Unmanaged` or `Managed`) overrides that, so check the project before relying on the default. For a plain unpacked folder, `txc environment solution pack` produces unmanaged unless you add `--managed`.

| Config | Solutions | Use for |
|---|---|---|
| `Debug` (`dotnet build -c Debug`) | **unmanaged** | dev/test environments that already hold unmanaged solutions |
| `Release` (`dotnet build -c Release`) | **managed** | staging/production |

**You cannot overwrite an unmanaged solution with a managed one (or vice versa).** A managed package (for example from `-c Release`) imported over an existing unmanaged solution fails with:

> You can't overwrite an unmanaged version of this solution with a managed version.

When deploying, first check what's already there:
1. Are the existing solutions on the environment managed or unmanaged? (`txc environment solution list --managed true` / `--managed false`, or `txc environment solution get <solution-name>`)
2. Build with the matching configuration.
3. On a mismatch, either uninstall the existing solution and re-import, or rebuild with the correct configuration.

## Pre-Flight Checks

Before deploying, validate:

| Check | Command |
|---|---|
| Auth/connection is valid | `txc config profile validate [<name>]` |
| Connected to correct environment | `txc config profile get [<name>]` |
| Solution can be safely removed | `txc environment solution uninstall-check <solution-name>` |

## Changeset Workflow

Mutating record, table, column, relationship and option-set commands take `--apply` (run now) or `--stage` (add the operation to a local changeset instead). Staged operations are kept in `.txc/changeset.json` of the current directory, so run the staging and the apply from the same directory.

| Command | Purpose |
|---|---|
| `txc environment changeset status` | Check current changeset state |
| `txc environment changeset apply --strategy batch\|transaction\|bulk` | Run the staged operations (`--continue-on-error` applies to `batch`) |
| `txc environment changeset discard` | Drop the staged operations (add `--yes` to skip the confirmation) |

Changesets let you group several operations and review them with `status` before running them.

## Troubleshooting Deployments

If import fails:
1. `txc environment deployment get --latest` — check error findings (or `--async-operation-id <id>` from the import output)
2. `txc environment component layer list --entity <logicalName>` (or `--id <guid> --type <type>`) — look for conflicting layers
3. `txc environment component dependency required --entity <logicalName>` (same selectors) — find missing dependencies
4. Fix locally, rebuild, and retry

## What NOT to Do

- ❌ Don't skip the local build step — XML errors are much faster to catch locally than at import time
- ❌ Don't deploy unmanaged solutions to production — use managed for proper versioning and clean uninstall
- ❌ Don't skip `txc environment solution publish` after import — UI changes (forms, views) remain invisible without it
- ❌ Don't retry a failed import without first checking `txc environment deployment get --latest` — you'll repeat the same error

See also: [troubleshooting](troubleshooting.md), [solution-layering](solution-layering.md)
