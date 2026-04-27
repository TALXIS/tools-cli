# Schema CRUD on Live Environments

## When to Use Environment Schema Tools

> **For development, always prefer local scaffolding** via `workspace_component_create`. Environment schema tools are for:
> - **Inspection** — understanding what's deployed in an environment
> - **Troubleshooting** — diagnosing schema issues in live environments
> - **Quick fixes** — emergency changes in non-production environments
> - **Prototyping** — rapid experimentation before codifying locally

## Entity (Table) Operations

| Tool | Operation |
|---|---|
| `environment_entity_list` | List all tables in the environment |
| `environment_entity_show` | Details of a specific table |
| `environment_entity_create` | Create a new table |
| `environment_entity_update` | Modify table properties |
| `environment_entity_delete` | Remove a table (check dependencies first!) |

### Before Deleting a Table
Always run `environment_component_dependency_delete_check` to find components that depend on this table. Deleting a table with dependencies will fail or cascade unexpectedly.

## Attribute (Column) Operations

| Tool | Operation |
|---|---|
| `environment_entity_attribute_list` | List columns on a table |
| `environment_entity_attribute_show` | Details of a specific column |
| `environment_entity_attribute_create` | Add a column to a table |
| `environment_entity_attribute_update` | Modify column properties |
| `environment_entity_attribute_delete` | Remove a column |

## Relationship Operations

| Tool | Operation |
|---|---|
| `environment_entity_relationship_list` | List relationships for a table |
| `environment_entity_relationship_show` | Relationship details |
| `environment_entity_relationship_create` | Create a new relationship |
| `environment_entity_relationship_update` | Modify relationship properties |
| `environment_entity_relationship_delete` | Remove a relationship |

## Option Set (Choice) Operations

| Tool | Operation |
|---|---|
| `environment_optionset_list` | List global option sets |
| `environment_optionset_show` | Option set details and values |
| `environment_optionset_create` | Create a new global option set |
| `environment_optionset_update` | Modify option set values |
| `environment_optionset_delete` | Remove an option set |

## Important Reminders

- ⚠️ Environment schema changes are **not tracked in source control** — they exist only in the environment
- ⚠️ Always publish after schema changes: `environment_solution_publish`
- ⚠️ Changes in managed layers can't be undone — only overridden by a new managed import
- ✅ For development work, use `workspace_component_create` instead (see [component-creation](component-creation.md))
- ✅ For inspection and understanding what's deployed, these tools are the right choice

See also: [component-creation](component-creation.md), [solution-layering](solution-layering.md), [troubleshooting](troubleshooting.md)
