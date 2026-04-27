# Schema CRUD on Live Environments

## When to Use Environment Schema Tools

> **For development, always prefer local scaffolding** via `workspace_component_create`. Environment schema tools are for:
> - **Inspection** — understanding what's deployed in an environment
> - **Troubleshooting** — diagnosing schema issues in live environments
> - **Quick fixes** — emergency changes in non-production environments
> - **Prototyping** — rapid experimentation before codifying locally

## Available Operations

Use `guide_environment` to discover environment schema tools and their parameters. Operations cover entities, attributes, relationships, and option sets (list, show, create, update, delete).

## Key Rules

- Always run `environment_component_dependency_delete_check` before deleting any component
- Always run `environment_solution_publish` after schema changes — they won't take effect without it
- Environment schema changes are **not tracked in source control** — codify locally afterward
- Changes in managed layers can't be undone — only overridden by a new managed import

## Decision Tree

- **Routine development** → use `workspace_component_create` (local, source-controlled)
- **Inspecting what's deployed** → use `environment_entity_*` tools
- **Emergency non-production fix** → use environment tools, then codify locally
- **Comparing deployed vs local** → use `environment_entity_attribute_list` against the table

## What NOT to Do

- ❌ Don't use these tools for routine development — changes bypass source control
- ❌ Don't delete tables/columns without running `environment_component_dependency_delete_check` first
- ❌ Don't forget to `environment_solution_publish` after schema changes

See also: [component-creation](component-creation.md), [solution-layering](solution-layering.md), [troubleshooting](troubleshooting.md)
