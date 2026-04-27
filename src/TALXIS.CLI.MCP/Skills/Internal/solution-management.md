# Solution Management Patterns

## Solution Types

### Unmanaged Solutions
- Used during **development**
- Components can be freely edited
- Changes are made directly in the active customization layer
- Not portable — don't move unmanaged solutions between environments

### Managed Solutions
- Used for **production** deployment
- Components are locked — can't be edited directly in the target
- Proper versioning and dependency tracking
- Can be cleanly uninstalled (if no dependencies)

## Solution Segmentation

Separate concerns into different solution projects:

| Solution | Contents | Example Name |
|---|---|---|
| Data Model | Tables, columns, relationships, option sets | `Solutions.DataModel` |
| Business Logic | Plugins, workflows, business rules | `Solutions.Logic` |
| User Interface | Forms, views, model-driven apps, sitemaps | `Solutions.UI` |
| Security | Security roles, field-level security | `Solutions.Security` |

### Benefits of Segmentation
- Independent deployment cycles
- Smaller import packages (faster deployments)
- Clearer ownership and change tracking
- Reduced merge conflicts in team development

## Component Ownership

### Inspecting What's in a Solution
```
Tool: environment_solution_component_list
```
Lists all components belonging to a specific solution.

### Layer Inspection
```
Tool: environment_component_layer_list → environment_component_layer_show
```
Shows the solution stack for a component — which solutions contribute customizations and in what order they're applied.

## Publisher Rules
- **One publisher per organization** — consistency across all solutions
- **Consistent prefix** — used for all tables, columns, and components
- **Maximum 8 characters** for the prefix (Dataverse platform limit)
- Example: publisher `contoso` with prefix `cont` → tables become `cont_tablename`

## Uninstall Safety

Before removing a solution:
```
Tool: environment_solution_uninstall_check
```
This checks for:
- Components that other solutions depend on
- Data that would be lost (table deletions)
- Customizations that would be removed

**Always run this check before uninstalling**, especially for managed solutions in production-adjacent environments.

## Solution Lifecycle
```
Create solution project locally
  → Add/scaffold components
    → Build and validate
      → Pack as managed (prod) or unmanaged (dev)
        → Import to target
          → Publish customizations
            → Verify deployment
```
