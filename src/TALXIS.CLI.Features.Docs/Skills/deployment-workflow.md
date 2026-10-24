# Deployment Workflow

## The Full Pipeline

```
local dev → build → pack → import → publish → verify
```

Each step has specific `txc` commands and checkpoints.

## Step-by-Step

### 1. Local Development
Use `txc workspace component create` to scaffold components. Edit XML files. Write plugin code. All local, all in source control.

### 2. Build
Build the solution project locally to catch errors early — malformed XML, missing references, schema violations.

### 3. Pack
```
txc environment solution pack
```
Creates a `.zip` solution file from your local project. This is still a local operation — no environment interaction yet.

### 4. Import
```
txc environment solution import
```
Uploads the solution package to the target Dataverse environment. By default returns immediately with an `asyncOperationId`. **Don't use `--wait`** — solution imports take minutes, and `--wait` blocks the command (and any agent running it) for all of that time. Instead, monitor progress with `txc environment deployment get --async-operation-id <id>` (the id is printed by `txc environment solution import`) until status is `Completed` or `Failed`. Never query the `asyncoperation` table directly via SQL — `txc environment deployment get` parses the findings for you.

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

The build configuration you pack/publish with decides whether the package contains **managed** or **unmanaged** solutions, and getting it wrong is a common deployment failure.

| Config | Solutions | Use for |
|---|---|---|
| `Debug` (`dotnet build`, `dotnet publish -c Debug`) | **unmanaged** | dev/test environments that already hold unmanaged solutions |
| `Release` (`dotnet publish -c Release`) | **managed** | staging/production |

**You cannot overwrite an unmanaged solution with a managed one (or vice versa).** A `-c Release` package imported over an existing unmanaged solution fails with:

> You can't overwrite an unmanaged version of this solution with a managed version.

When deploying, first check what's already there:
1. Are the existing solutions on the environment managed or unmanaged? (`txc environment solution uninstall-check` / inspect the environment)
2. Build with the matching configuration.
3. On a mismatch, either uninstall the existing solution and re-import, or rebuild with the correct configuration.

## Pre-Flight Checks

Before deploying, validate:

| Check | Command |
|---|---|
| Auth/connection is valid | `txc config profile validate` |
| Connected to correct environment | `txc config profile get` |
| Solution can be safely updated/removed | `txc environment solution uninstall-check` |

## Changeset Workflow

For environments that support staged deployments:

| Command | Purpose |
|---|---|
| `txc environment changeset status` | Check current changeset state |
| `txc environment changeset apply` | Commit staged changes |
| `txc environment changeset discard` | Rollback staged changes |

Changesets let you group multiple imports and verify before committing.

## Troubleshooting Deployments

If import fails:
1. `txc environment deployment get --latest` — check error findings (or `--async-operation-id <id>` from the import output)
2. `txc environment component layer list` — look for conflicting layers
3. `txc environment component dependency required` — find missing dependencies
4. Fix locally, rebuild, and retry

## What NOT to Do

- ❌ Don't skip the local build step — XML errors are much faster to catch locally than at import time
- ❌ Don't deploy unmanaged solutions to production — use managed for proper versioning and clean uninstall
- ❌ Don't skip `txc environment solution publish` after import — UI changes (forms, views) remain invisible without it
- ❌ Don't retry a failed import without first checking `txc environment deployment get --latest` — you'll repeat the same error

See also: [troubleshooting](troubleshooting.md), [solution-layering](solution-layering.md)
