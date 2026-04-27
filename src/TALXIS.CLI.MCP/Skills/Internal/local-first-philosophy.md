# Local-First Philosophy

## Decision Tree: Local vs Live Environment Operations

### Key Principle
Local operations are **instant and reversible**. Environment operations take **30 seconds to 5 minutes** and may have side effects. Always prefer local-first.

### When the User Says…

| User Request | Correct Action | Tool | Why |
|---|---|---|---|
| "Create a table" | Scaffold locally | `workspace_component_create` | Creates XML files, no environment needed |
| "Add a column" | Edit local XML | `workspace_component_create` or manual XML edit | Keeps changes in source control |
| "Create a form" | Scaffold locally | `workspace_component_create` | Forms are XML definitions |
| "Check what exists" | Inspect workspace OR environment | `workspace_explain` (building) / `environment_entity_list` (troubleshooting) | Depends on context |
| "Deploy my changes" | Pack and import | `environment_solution_pack` → `environment_solution_import` | Only after local validation |

### Use Local Workspace Operations When:
- Creating or modifying schema (tables, columns, relationships)
- Building forms, views, sitemaps
- Writing plugin code
- Organizing solution components
- Any development task where you're **building something new**

### Use Live Environment Operations Only When:
1. **Inspection/troubleshooting** — checking what's deployed, comparing layers, diagnosing issues
2. **Deployment** — after local build validation passes
3. **Data operations** — querying, migrating, or seeding data (can only be done live)
4. **Quick fixes** — emergency schema changes in non-production environments

### The Inner Loop
```
scaffold locally → edit XML → build → pack → import → publish → verify
         ^                                                    |
         └────────────── iterate ─────────────────────────────┘
```

### Anti-Patterns to Avoid
- ❌ Using `environment_entity_create` for development — changes aren't in source control
- ❌ Using `environment_entity_attribute_create` to add columns — not tracked locally
- ❌ Deploying without building first — catch errors early
- ❌ Modifying production environments directly — always go through solutions
