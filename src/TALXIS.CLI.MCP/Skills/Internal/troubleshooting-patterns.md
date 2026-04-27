# Troubleshooting Decision Trees

## Deployment Failure
```
Deployment failed
  └─→ environment_deployment_show --latest
       ├─→ Findings show component error
       │    └─→ Check specific component with environment_component_layer_list
       ├─→ Findings show missing dependency
       │    └─→ environment_component_dependency_required → identify what's needed
       └─→ Findings show timeout/generic error
            └─→ Retry import with --wait, check environment health
```

## Component Conflict
```
Component behaves unexpectedly or can't be modified
  └─→ environment_component_layer_list (componentId)
       ├─→ Multiple solutions own this component
       │    └─→ environment_component_layer_show → understand each layer
       │         └─→ Determine which solution should own it
       └─→ Managed layer is blocking changes
            └─→ Must update the managed solution, not the component directly
```

## Can't Delete a Component
```
Delete fails with dependency error
  └─→ environment_component_dependency_delete_check
       ├─→ Other components reference this one
       │    └─→ Remove references first, then retry delete
       └─→ Solution dependency exists
            └─→ Update or remove the depending solution first
```

## Missing Dependency
```
Import fails with missing dependency
  └─→ environment_component_dependency_required
       ├─→ Required component is in another solution
       │    └─→ Import that solution first
       └─→ Required component doesn't exist
            └─→ Create it locally → deploy → then retry original import
```

## Authentication Issues
```
Commands fail with auth/connection errors
  └─→ config_profile_validate
       ├─→ Profile invalid
       │    └─→ config_profile_show → check URL and connection
       │         └─→ config_connection_show → verify auth method
       │              └─→ Re-create or update credentials
       └─→ Profile valid but wrong environment
            └─→ config_profile_show → verify environment URL
                 └─→ Switch to correct profile or create new one
```

## Wrong Environment Connected
```
Data or schema doesn't match expectations
  └─→ config_profile_show
       ├─→ URL points to wrong environment
       │    └─→ Switch profile or create profile for correct environment
       └─→ URL is correct but data is stale
            └─→ Check if publish is needed: environment_solution_publish
```

## General Diagnostic Order
1. **Verify connection**: `config_profile_validate`
2. **Check deployment status**: `environment_deployment_show --latest`
3. **Inspect component layers**: `environment_component_layer_list`
4. **Check dependencies**: `environment_component_dependency_required` or `_delete_check`
5. **Review solution contents**: `environment_solution_component_list`
