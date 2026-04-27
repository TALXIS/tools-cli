# Troubleshooting Common Issues

## Deployment Failures

**Start here:** `environment_deployment_show --latest`

```
Import failed
  ├─→ Error: Component conflict
  │    └─→ environment_component_layer_list (componentId)
  │         └─→ Identify which solution owns the conflicting component
  │              └─→ Update that solution first, or remove the conflict
  │
  ├─→ Error: Missing dependency
  │    └─→ environment_component_dependency_required
  │         └─→ Import the solution containing the required component first
  │
  ├─→ Error: Solution already exists (version conflict)
  │    └─→ Increment the solution version and retry
  │
  └─→ Error: Timeout or generic failure
       └─→ Check environment health, retry with --wait flag
```

## Component Conflicts

When a component behaves unexpectedly or modifications don't take effect:

1. **Inspect layers**: `environment_component_layer_list` — see all solutions that customize this component
2. **Examine specific layer**: `environment_component_layer_show` — understand what each solution contributes
3. **Resolution**: The topmost layer wins. Either:
   - Update the topmost managed solution
   - Remove the unmanaged active layer
   - Reorder solution imports

## Authentication Issues

```
Commands fail with 401/403 or connection errors
  └─→ config_profile_validate
       ├─→ "Invalid" → config_profile_show to check URL
       │    └─→ config_connection_show to verify auth credentials
       │         └─→ Re-create auth: config_auth_add-service-principal
       │
       └─→ "Valid" but still failing
            └─→ Check if the service principal has required security roles
                 └─→ Verify environment-level access in Power Platform admin center
```

## Dependency Problems

### Can't Delete a Component
```
Tool: environment_component_dependency_delete_check
```
Shows what depends on the component you're trying to delete. Remove or update those dependencies first.

### Missing Required Dependencies
```
Tool: environment_component_dependency_required
```
Shows what components are required by a given component. Ensure all dependencies are present in the target environment.

## Wrong Environment

If data or schema doesn't match expectations:
1. `config_profile_show` — verify the environment URL
2. Confirm you're connected to dev/test/prod as intended
3. Switch profiles if needed

## Quick Reference

| Symptom | First Tool to Run |
|---|---|
| Import failed | `environment_deployment_show --latest` |
| Component conflict | `environment_component_layer_list` |
| Can't delete something | `environment_component_dependency_delete_check` |
| Missing dependency | `environment_component_dependency_required` |
| Auth errors | `config_profile_validate` |
| Wrong data showing | `config_profile_show` |
| Changes not visible | `environment_solution_publish` |

See also: [deployment-workflow](deployment-workflow.md), [solution-layering](solution-layering.md)
