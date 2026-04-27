# Build Error Recovery

When building a Power Platform solution locally, the TALXIS DevKit build system validates all components against XSD schemas and structural rules. This skill helps diagnose and fix common build errors.

## Error Codes

### TALXISXSD001 — XML Schema Validation Failed
**Cause:** A component XML file doesn't match the expected schema.
**Common triggers:**
- Entity name exceeds 50 characters or is empty
- Missing required `LocalizedNames` element on a component
- Entity.xml has non-empty `<FormXml>` or `<SavedQueries>` (these must be empty — forms/views go in separate files)
- Invalid characters in name (must match `[a-zA-Z0-9_]+`)
- Missing required attributes (e.g., `Name` on entity, `savedqueryid` on view)
**Fix:** Read the error location (file:line), check the element against the expected structure. Re-scaffold the component if the structure is badly corrupted.

### TALXISGUID001 — Duplicate GUID Detected
**Cause:** Two or more components share the same GUID across files.
**Common triggers:** Copy-pasting components without regenerating GUIDs.
**Fix:** Generate new GUIDs for the duplicate entries. Check: formid, savedqueryid, WebResourceId, WorkflowId, SdkMessageProcessingStepId, connectionroleid, OptionSetId, AppModuleId, RoleId.

### TALXISQF001 — Quick Find View Missing Filter (Warning)
**Cause:** A view marked `isquickfindquery="1"` lacks the required `<filter isquickfindfields="1">` with conditions.
**Fix:** Add a quick find filter with at least one condition specifying which columns are searchable.

### TALXISPCF001 — PCF Control Not Provided (Warning)
**Cause:** A form references a PCF custom control that isn't provided by any solution in the package.
**Fix:** Add the solution containing the PCF control as a dependency, or remove the control reference from the form.

### TALXISJSONSCHEMA001 — Flow JSON Invalid
**Cause:** A Power Automate flow definition doesn't match the expected schema.
**Fix:** Ensure `properties.definition` contains `parameters`, `triggers`, and `actions`.

## Structural Rules
- Entity/attribute names: 1–50 characters, pattern `[a-zA-Z0-9_]+`
- GUIDs: pattern `{?[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}}?`
- LocalizedNames required on: entities, forms, views, app modules, web resources, workflows, plugins, roles
- Entity.xml: `<FormXml>` and `<SavedQueries>` elements must be empty (forms/views are in separate files)
