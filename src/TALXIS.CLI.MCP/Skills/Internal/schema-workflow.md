# Schema Creation & Modification Workflow

## Step-by-Step: Creating or Modifying Dataverse Schema Locally

### Step 1: Discover Available Component Types
```
Tool: workspace_component_type_list
```
Returns the list of component types that can be scaffolded (Entity, Attribute, Form, View, OptionSet, Relationship, etc.).

### Step 2: Get Required Parameters
```
Tool: workspace_component_parameter_list
Parameters: { componentType: "<type from step 1>" }
```
Returns parameter names, types, whether required, and default values.

### Step 3: Scaffold the Component
```
Tool: workspace_component_create
Parameters: {
  componentType: "<type>",
  SolutionRootPath: "Declarations",
  ...other required params
}
```

**Important:** Always pass `SolutionRootPath=Declarations` unless the user specifies a different solution project. This is the default convention for schema components.

### Step 4: Customize Generated XML
After scaffolding, the tool creates XML files in the solution project. Common customizations:
- Edit display names and descriptions
- Set additional column properties (required level, searchable, etc.)
- Configure form layouts
- Add view columns and filters

### Step 5: Build Locally to Validate
Build the solution project to catch XML errors, missing references, or schema violations before deploying.

### Step 6: Deploy
```
1. environment_solution_pack   → Creates .zip from local files
2. environment_solution_import → Uploads to target environment (use --wait)
3. environment_solution_publish → Publishes customizations
```

## Common Component Creation Patterns

### New Table with Columns
1. Scaffold Entity → creates table XML
2. Scaffold Attributes → adds columns to the table
3. Scaffold Form → creates default form layout
4. Scaffold View → creates default view

### Adding to Existing Table
1. Use `workspace_explain` to understand current structure
2. Scaffold only the new Attribute/Form/View
3. Build and deploy

### Relationships
1. Scaffold the relationship component
2. Verify both tables exist in the solution
3. Build to validate referential integrity
