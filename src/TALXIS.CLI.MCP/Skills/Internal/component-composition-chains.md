# Component Composition Chains

<!-- Internal reasoning skill: contains decision trees for multi-step component creation workflows. -->
<!-- Components form chains where order matters and earlier steps produce IDs needed by later steps. -->

## Table Creation Chain

```
START: User wants to create a new table
│
├─ 1. workspace_component_create (componentType: "Entity", SolutionRootPath: "Declarations")
│     → Creates Entity.xml with system attributes (primaryid, primaryname, statecode, statuscode, etc.)
│     → CHECK: Entity.xml exists in Declarations/Entities/{logicalname}/
│
├─ 2. workspace_component_create (componentType: "Attribute", ...) — REPEAT for each column
│     → Adds attribute XML to the entity folder
│     → CHECK: Attribute XML file exists, correct data type used
│     → NOTE: String, WholeNumber, Decimal, Float, Currency, DateAndTime, DateOnly,
│             Lookup, Customer, OptionSet, MultiSelectOptionSet, TwoOptions, Memo
│     → If OptionSet: also needs option values scaffolded
│     → If Lookup: needs target entity specified
│
├─ 3. workspace_component_create (componentType: "Form", ...)
│     → Creates form XML with Tab→Column→Section→Row→Cell→Control hierarchy
│     → CHECK: Form XML exists, controls reference valid attributes from step 2
│     → CHECK: Control ClassIDs are correct (see form-xml-reference skill)
│
├─ 4. workspace_component_create (componentType: "View", ...)
│     → Creates SavedQuery XML with FetchXml and LayoutXml
│     → CHECK: View references valid attributes, querytype is correct (0=public, 64=quick-find)
│
└─ 5. OPTIONAL: Add to app → sitemap entry → security role privileges
      → Only if the entity should appear in a model-driven app navigation

COMMON MISTAKES:
- Creating form before attributes → form controls reference non-existent fields
- Forgetting primaryname attribute → entity has no display name field
- Using environment tools instead of workspace tools → changes not in source control
```

## Plugin Development Chain

```
START: User wants to create a plugin
│
├─ 1. workspace_component_create (componentType: "PluginProject", SolutionRootPath: "Declarations")
│     → Creates .csproj with ProjectType=Plugin, SignAssembly=true, PluginBase.cs
│     → CHECK: .csproj exists in src/Plugins.{Domain}/
│     → CHECK: PluginBase.cs exists with LocalPluginContext pattern
│
├─ 2. Write the plugin class manually — {Action}{Entity}Plugin.cs
│     → Inherits PluginBase, registers step handlers
│     → Use Pre-validation(10) for validation, Pre-operation(20) for field calc, Post-operation(40) for side effects
│     → CHECK: Class compiles, stage value is correct for the use case
│
├─ 3. workspace_component_create (componentType: "PluginAssembly", SolutionRootPath: "Declarations")
│     → Registers assembly in solution XML with IsolationMode, SourceType
│     → CHECK: Assembly registration XML exists in solution
│     → MUST come after step 1 — references the project output
│
├─ 4. workspace_component_create (componentType: "PluginStep", SolutionRootPath: "Declarations")
│     → Registers individual step with Stage, SdkMessage, FilteringAttributes
│     → CHECK: Step XML references correct assembly from step 3
│     → CHECK: SDK message GUID matches intended operation (Create/Update/Delete/etc.)
│     → REPEAT for each message+entity combination
│
└─ 5. OPTIONAL: workspace_component_create (componentType: "PluginTest", ...)
      → Creates FakeXrmEasy test base for unit testing
      → Should test the logic from step 2

COMMON MISTAKES:
- Registering step before assembly → step has no parent
- Wrong stage for use case → validation in Post-operation (data already saved)
- Missing SignAssembly → Dataverse rejects the assembly
- Hardcoding SDK message GUIDs from memory → use reference table from plugin-development skill
```

## Form Modification Chain

```
START: User wants to modify an existing form
│
├─ 1. workspace_explain → Identify the form XML file location
│     → CHECK: Form XML file exists in the workspace
│     → NOTE: Forms are in Entities/{logicalname}/FormXml/{formtype}/
│
├─ 2. Determine insertion point
│     → Read existing form XML to find target tab/section/row
│     → Decide: Add tab? Add section to existing tab? Add field to existing section?
│
├─ IF adding a new field to existing section:
│  ├─ 3a. Locate target <section> by name/ID
│  ├─ 3b. Add <row><cell><control .../></cell></row> inside the section's <rows>
│  ├─ 3c. Set correct ClassID from form-xml-reference skill
│  └─ 3d. Set datafieldname to the attribute's logical name
│
├─ IF adding a new section:
│  ├─ 3a. Locate target <column> in target <tab>
│  ├─ 3b. Add <section> with <labels>, generate GUID for id
│  └─ 3c. Add rows/cells/controls inside the new section
│
├─ IF adding a new tab:
│  ├─ 3a. Add <tab> to form's tab collection
│  ├─ 3b. Include <columns> with at least one <column>
│  ├─ 3c. Include <sections> with at least one <section>
│  └─ 3d. Set IsUserDefined="1" for custom tabs
│
└─ 4. Validate form XML structure
      → CHECK: All ClassIDs are valid (reference form-xml-reference skill)
      → CHECK: Hierarchy is complete — no skipped levels
      → CHECK: All referenced datafieldnames exist as attributes on the entity
      → CHECK: All id/labelid GUIDs are unique within the form

COMMON MISTAKES:
- Skipping hierarchy levels (control directly in tab) → form won't render
- Wrong ClassID for field type → control renders incorrectly or errors
- Referencing non-existent attribute in datafieldname → runtime error
- Duplicate GUIDs in id attributes → unpredictable behavior
```

## Custom API Chain

```
START: User wants to create a custom API
│
├─ 1. Create backing plugin FIRST (follow Plugin Development Chain above)
│     → The plugin reads InputParameters and sets OutputParameters
│     → CHECK: Plugin compiles, handles expected parameters
│
├─ 2. workspace_component_create (componentType: "CustomAPI", SolutionRootPath: "Declarations")
│     → Creates customapi.xml with uniquename, bindingtype, isfunction, plugintypeid
│     → CHECK: Plugin type GUID references the plugin from step 1
│     → Naming: {prefix}_{apilogicalname}
│
├─ 3. workspace_component_create (componentType: "CustomAPIRequestParameter", ...) — REPEAT
│     → Adds request parameter XML with type code, optional flag
│     → Naming: {prefix}_{apiname}.{ParamName}
│     → CHECK: Type codes match the plugin's expected InputParameters
│
└─ 4. workspace_component_create (componentType: "CustomAPIResponseProperty", ...) — REPEAT
      → Adds response property XML with type code
      → Naming: {prefix}_{apiname}.{PropertyName}
      → CHECK: Type codes match the plugin's OutputParameters

COMMON MISTAKES:
- Creating API definition before the backing plugin → plugintypeid is empty
- Mismatched parameter names between API XML and plugin code → runtime errors
- Wrong type codes → serialization failures
- Forgetting customization prefix → naming convention violation
```

## BPF Creation Chain

```
START: User wants to create a business process flow
│
├─ 1. workspace_component_create (componentType: "BPF", SolutionRootPath: "Declarations")
│     → Creates BPF entity (IsBPFEntity=1) with special attributes + XAML workflow
│     → CHECK: Entity.xml has IsBPFEntity=1
│     → CHECK: XAML workflow file exists with Category=4, TriggerOnCreate=1
│
├─ 2. workspace_component_create (componentType: "BPFStage", ...) — REPEAT for each stage
│     → Adds stage to XAML workflow
│     → Specify stage name, category, target entity
│     → CHECK: Stage appears in XAML, references valid entity
│
└─ 3. workspace_component_create (componentType: "BPFStageStep", ...) — REPEAT per stage
      → Adds steps within a stage
      → Bind to specific attribute, set IsRequired flag
      → CHECK: Referenced attribute exists on the stage's entity
      → CHECK: Step sequence is correct within the stage

COMMON MISTAKES:
- Adding stages before BPF entity exists → XAML workflow missing
- Referencing attributes from wrong entity in cross-entity BPF
- Forgetting IsBPFEntity=1 → Dataverse treats it as normal entity
- Circular branching paths → BPF won't validate
```

## Local vs Live Decision

```
ALL CHAINS ABOVE: Use workspace tools (local, instant, reversible)
│
├─ workspace_component_create → local scaffolding
├─ workspace_explain → inspect local state
├─ Edit XML files directly → fine-tune generated output
│
ONLY use live environment tools for:
├─ environment_entity_list → check what's deployed (inspection)
├─ environment_component_layer_list → troubleshoot conflicts
├─ environment_solution_import → deploy after local build
└─ environment_solution_publish → activate changes after import
```
