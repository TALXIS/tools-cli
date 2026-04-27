# Creating Dataverse Components

## Key Concept

Scaffolding creates **local files** in your workspace — it does NOT create components in a live Dataverse environment. This is the recommended approach for development because changes are tracked in source control and can be reviewed before deployment.

## Step-by-Step Workflow

### 1. Understand Your Workspace First
```
Tool: workspace_explain
```
Before creating components, understand what already exists in your repository — which solution projects, what components are already scaffolded, and where new components should go.

### 2. Discover Available Component Types
```
Tool: workspace_component_type_list
```
Returns all component types you can scaffold: Entity, Attribute, Form, View, OptionSet, Relationship, and more.

### 3. Get Required Parameters
```
Tool: workspace_component_parameter_list
Parameters: { componentType: "<type>" }
```
Each component type has different required and optional parameters. This tool shows you exactly what's needed.

### 4. Scaffold the Component
```
Tool: workspace_component_create
Parameters: {
  componentType: "<type>",
  SolutionRootPath: "Declarations",
  ...required parameters
}
```

**Default convention:** Always pass `SolutionRootPath=Declarations` unless the user specifies a different solution project path.

### 5. Customize the Generated XML
After scaffolding, XML files are created in the solution project. You can edit these to:
- Adjust display names and descriptions
- Set column properties (required level, searchable, format)
- Configure form layouts and sections
- Define view columns, filters, and sort orders

### 6. Build and Deploy
Build locally to validate, then follow the [deployment workflow](deployment-workflow.md).

## Common Scenarios

### Creating a New Table
1. Scaffold Entity — creates the table definition XML
2. Scaffold Attributes — adds columns to the table
3. Scaffold a Form — creates a default form for the table
4. Scaffold a View — creates a default view

### Adding a Column to an Existing Table
1. `workspace_explain` to find the table's solution project
2. `workspace_component_parameter_list` for Attribute parameters
3. `workspace_component_create` with the table reference and column details

**Example:** Adding a "Status" option set column to an existing Order table:
```
workspace_component_parameter_list { componentType: "Attribute" }
workspace_component_create {
  componentType: "Attribute",
  SolutionRootPath: "Declarations",
  EntityLogicalName: "prefix_order",
  LogicalName: "prefix_orderstatus",
  DisplayName: "Order Status",
  AttributeType: "Picklist"
}
```

### Creating a Relationship
1. Ensure both tables exist in the workspace
2. `workspace_component_parameter_list` for the Relationship type
3. `workspace_component_create` with source/target table references

**Example:** Creating a many-to-one relationship (Order → Customer):
```
workspace_component_create {
  componentType: "Relationship",
  SolutionRootPath: "Declarations",
  PrimaryEntity: "prefix_order",
  RelatedEntity: "prefix_customer",
  RelationshipType: "ManyToOne"
}
```

## What NOT to Do

- ❌ Don't use `environment_entity_create` for development — it bypasses source control
- ❌ Don't use `environment_entity_attribute_create` to add columns during development
- ❌ Don't scaffold a Form or View before the Entity and its Attributes exist — XML references will break
- ❌ Don't scaffold a Relationship if the target table hasn't been created yet
- ✅ Use environment tools only for inspection, troubleshooting, or emergency fixes

See also: [project-structure](project-structure.md), [schema-management](schema-management.md)
