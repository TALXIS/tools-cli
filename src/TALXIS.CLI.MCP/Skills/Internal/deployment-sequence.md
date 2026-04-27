# Deployment Sequence — Decision Logic

<!-- Internal reasoning skill: contains ONLY sequencing rules and failure recovery. -->
<!-- For tool descriptions and parameters, see the public deployment-workflow skill. -->

## Mandatory Order (never skip or reorder)
```
1. Build locally              — catch errors before touching environment
2. config_profile_validate    — confirm auth + target env BEFORE packing
3. environment_solution_pack  — local operation, creates .zip
4. environment_solution_import (--wait) — blocks until complete
5. environment_solution_publish — required for UI changes to take effect
6. environment_deployment_show --latest — verify success
```

## Pre-Flight Decision Tree
```
Before deploying:
  ├─→ Is the local build clean?
  │    ├─ YES → proceed
  │    └─ NO → fix build errors FIRST, never deploy broken builds
  ├─→ Is the profile validated?
  │    ├─ YES → proceed
  │    └─ NO → config_profile_validate, then config_profile_show to confirm target
  └─→ Dev or prod target?
       ├─ DEV → unmanaged import is fine
       └─ PROD/UAT → managed import ONLY, never unmanaged
```

## Failure Recovery Sequence
```
Import failed
  └─→ environment_deployment_show --latest
       ├─→ Component error → environment_component_layer_list → resolve conflict
       ├─→ Missing dependency → environment_component_dependency_required → import dependency first
       ├─→ Version conflict → increment solution version → retry
       └─→ Timeout/generic → retry with --wait, check env health
```
→ ALWAYS check deployment findings before retrying blindly
→ NEVER retry more than twice without diagnosing the root cause

## Changeset vs Direct Import
→ USE changesets (`environment_changeset_status` → make changes → `environment_changeset_apply`) when:
  - Multiple solutions need to be imported atomically
  - You want a rollback option (`environment_changeset_discard`)
→ USE direct import when:
  - Single solution deployment
  - CI/CD pipeline (changesets add unnecessary complexity)

## Anti-Patterns
- ❌ Deploying without building first → XML errors only caught at import (slow feedback)
- ❌ Skipping `environment_solution_publish` → UI changes invisible to users
- ❌ Deploying unmanaged to production → can't cleanly uninstall, no version tracking
- ❌ Retrying failed imports without checking `environment_deployment_show` → repeating the same error
