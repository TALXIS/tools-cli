# Deployment Sequence

## Correct Deployment Order

### 1. Validate Local Build
Ensure the solution project compiles without errors. Catch XML issues, missing references, and schema violations before touching the environment.

### 2. Pack the Solution
```
Tool: environment_solution_pack
```
Creates a `.zip` solution file from the local project files. This is a local operation that prepares the artifact for import.

### 3. Import to Target Environment
```
Tool: environment_solution_import
Recommended: use --wait flag
```
Uploads and processes the solution in the target Dataverse environment. The `--wait` flag blocks until import completes, giving you immediate feedback on success/failure.

### 4. Publish Customizations
```
Tool: environment_solution_publish
```
Makes imported changes visible to users. Without publishing, form/view changes remain in draft state.

### 5. Verify Deployment
```
Tool: environment_deployment_show --latest
```
Check the latest deployment status and findings. Look for warnings or errors that may need attention.

### 6. Troubleshoot Failures
If deployment fails, use these tools in order:

| Symptom | Tool | What to Look For |
|---|---|---|
| Import failed | `environment_deployment_show` | Error findings, component failures |
| Component conflict | `environment_component_layer_list` | Which solution owns the component |
| Can't overwrite | `environment_component_layer_show` | Active layer details |
| Missing dependency | `environment_component_dependency_required` | Required components not in target |
| Can't delete component | `environment_component_dependency_delete_check` | Blocking dependencies |

## Pre-Deployment Checklist
- [ ] Local build succeeds
- [ ] Target profile is validated (`config_profile_validate`)
- [ ] Connected to correct environment (`config_profile_show`)
- [ ] Unmanaged solution exists in target (for dev) or managed import planned (for prod)

## Changeset Workflow (Optional)
For staged deployments:
1. `environment_changeset_status` — check current changeset state
2. Make changes via import
3. `environment_changeset_apply` — commit changes
4. Or `environment_changeset_discard` — rollback if issues found
