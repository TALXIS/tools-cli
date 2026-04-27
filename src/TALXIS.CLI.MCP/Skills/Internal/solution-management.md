# Solution Management — Decision Logic

<!-- Internal reasoning skill: contains ONLY solution-type decisions and segmentation routing. -->
<!-- For solution concepts, layer details, and inspection tools, see the public solution-layering skill. -->

## Managed vs Unmanaged Decision
```
Where is the user deploying?
  ├─ Development environment  → unmanaged import (editable, iterative)
  ├─ Test/UAT environment     → managed import (locked, production-like)
  ├─ Production environment   → managed import ONLY (never unmanaged)
  └─ Unsure                   → ask user, default to managed for safety
```

## Component-to-Solution Routing
```
What is the user creating?
  ├─ Table, column, relationship, option set  → Solutions.DataModel
  ├─ Plugin, workflow, business rule          → Solutions.Logic
  ├─ Form, view, model-driven app, sitemap   → Solutions.UI
  ├─ Security role, field-level security      → Solutions.Security
  └─ Unsure which solution                   → workspace_explain to discover existing structure
```
→ NEVER put all components in one solution — always segment by concern
→ IF the repo already has different naming, follow its conventions (check with `workspace_explain`)

## Uninstall Safety Decision
```
User wants to remove/uninstall a solution:
  → ALWAYS: environment_solution_uninstall_check BEFORE uninstalling
  → IF dependencies found: resolve dependencies first
  → IF data loss warning: confirm with user explicitly
  → NEVER uninstall in production without checking first
```

## Layer Conflict Resolution
```
Component behaves unexpectedly:
  → environment_component_layer_list (see all layers)
    ├─ Multiple managed solutions → topmost wins; update that solution
    ├─ Unmanaged active layer blocking → remove active customization
    └─ Single layer, still wrong → check if publish was skipped
```

## Anti-Patterns
- ❌ Deploying unmanaged to production → can't track versions, can't cleanly uninstall
- ❌ All components in one mega-solution → slow deployments, merge conflicts, unclear ownership
- ❌ Uninstalling without `environment_solution_uninstall_check` → cascade failures or data loss
- ❌ Moving unmanaged solutions between environments → use managed for transport
