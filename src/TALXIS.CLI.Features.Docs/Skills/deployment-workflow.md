# Deployment Workflow

## The Full Pipeline

```
local dev → build → pack → import → publish → verify
```

Each step has specific `txc` tools and checkpoints.

## Step-by-Step

### 1. Local Development
Use `workspace_component_create` to scaffold components. Edit XML files. Write plugin code. All local, all in source control.

### 2. Build
Build the solution project locally to catch errors early — malformed XML, missing references, schema violations.

### 3. Pack
```
Tool: environment_solution_pack
```
Creates a `.zip` solution file from your local project. This is still a local operation — no environment interaction yet.

### 4. Import
```
Tool: environment_solution_import
```
Uploads the solution package to the target Dataverse environment. **Recommended:** use the `--wait` flag to block until import completes and get immediate feedback.

### 5. Publish
```
Tool: environment_solution_publish
```
Publishes all customizations. Without this step, changes to forms, views, and other UI components won't be visible to users.

### 6. Verify
```
Tool: environment_deployment_show --latest
```
Check the deployment status and review any findings (warnings, errors, informational messages).

## Pre-Flight Checks

Before deploying, validate:

| Check | Tool |
|---|---|
| Auth/connection is valid | `config_profile_validate` |
| Connected to correct environment | `config_profile_show` |
| Solution can be safely updated/removed | `environment_solution_uninstall_check` |

## Changeset Workflow

For environments that support staged deployments:

| Tool | Purpose |
|---|---|
| `environment_changeset_status` | Check current changeset state |
| `environment_changeset_apply` | Commit staged changes |
| `environment_changeset_discard` | Rollback staged changes |

Changesets let you group multiple imports and verify before committing.

## Troubleshooting Deployments

If import fails:
1. `environment_deployment_show --latest` — check error findings
2. `environment_component_layer_list` — look for conflicting layers
3. `environment_component_dependency_required` — find missing dependencies
4. Fix locally, rebuild, and retry

## What NOT to Do

- ❌ Don't skip the local build step — XML errors are much faster to catch locally than at import time
- ❌ Don't deploy unmanaged solutions to production — use managed for proper versioning and clean uninstall
- ❌ Don't skip `environment_solution_publish` after import — UI changes (forms, views) remain invisible without it
- ❌ Don't retry a failed import without first checking `environment_deployment_show --latest` — you'll repeat the same error

See also: [troubleshooting](troubleshooting.md), [solution-layering](solution-layering.md)
