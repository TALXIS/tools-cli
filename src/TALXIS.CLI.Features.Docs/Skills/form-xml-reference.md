# Form XML Structure Reference

## Key Concept

Forms are scaffolded via `pp-form-*` templates. Templates generate correct XML; the build validates it. Use `workspace_component_create` with the appropriate form template rather than writing form XML manually.

## Form XML Hierarchy

All forms follow a strict 6-level nesting: **Tab → Column → Section → Row → Cell → Control**. Skipping levels produces invalid XML.

## Form Types

- **Main Form** — default editing form, full hierarchy, multiple tabs, event handlers, business rules
- **Dialog Form** — popup context, single tab, includes `<tabfooter>` for action buttons
- **Quick Create Form** — compact inline creation, single tab, essential fields only
- **Quick View Form** — read-only, displayed inside another form's lookup field

## Form Fragments

Customizations add XML fragments at specific insertion points rather than replacing the whole form. Each fragment targets a parent element by name/ID and specifies position relative to existing children.

## Scaffolding Workflow

1. Scaffold the Entity and Attributes **before** scaffolding a Form
2. Use `workspace_component_create` with form templates (`pp-form-main`, `pp-form-dialog`, etc.)
3. Templates handle ClassIDs, hierarchy, and required attributes automatically
4. Call `workspace_component_parameter_list` for template-specific parameters

## What NOT to Do

- ❌ Don't skip hierarchy levels (e.g., control directly inside tab without section/row/cell)
- ❌ Don't write form XML manually — use `pp-form-*` templates
- ❌ Don't scaffold a Form before the Entity and its Attributes exist
- ❌ Don't forget `<labels>` elements — they're required for localization

See also: [component-creation](component-creation.md), [schema-management](schema-management.md)
