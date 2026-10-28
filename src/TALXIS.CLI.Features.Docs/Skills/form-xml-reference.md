# Form XML Structure Reference

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## Key Concept

Forms are scaffolded via `pp-form-*` templates. Templates generate correct XML; the build validates it. Use `txc workspace component create <template> --output <project-dir>` with the appropriate form template (see below) rather than writing form XML manually. `<project-dir>` is the folder of the solution project that owns the form.

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
2. Run `txc workspace component create pp-entity-form --output <project-dir> --param FormType=main --param EntitySchemaName=<prefix_table> --param FormId=<form-guid>` (`FormType` is `main`, `dialog`, or `quickCreate`; without `FormId` a random id is generated and printed)
3. Templates handle ClassIDs, hierarchy, and required attributes automatically
4. Run `txc workspace component parameter list pp-entity-form` (or any other template name) for template-specific parameters

## What NOT to Do

- ❌ Don't skip hierarchy levels (e.g., control directly inside tab without section/row/cell)
- ❌ Don't write form XML manually — use `pp-form-*` templates
- ❌ Don't scaffold a Form before the Entity and its Attributes exist
- ❌ Don't forget `<labels>` elements — they're required for localization

## Form Scaffolding Chain

Build forms top-down. Each level is a separate template call:

1. **`pp-entity-form`** — Create the form (main, dialog, or quickCreate)
2. **`pp-form-tab`** — Add a tab (use `RemoveDefaultTab=True` to replace the empty default)
3. **`pp-form-column`** — Add a column inside the tab
4. **`pp-form-section`** — Add a section inside the column
5. **`pp-form-row`** — Add a row inside the section (one per field)
6. **`pp-form-cell`** — Add a cell inside the row (provides the label)
7. **`pp-form-control`** — Add the field control inside the cell

The fragment templates `pp-form-tab` to `pp-form-control` locate the form through `FormId`, `FormType` and `EntitySchemaName`. The templates do not mark them required, but their defaults are placeholders, so pass all three. Use the same `FormId` that `pp-entity-form` got (generate one GUID up front, or read it from the existing form file), the same `EntitySchemaName`, and `FormType` `main`, `quick` or `dialog` (`quick` here, where `pp-entity-form` says `quickCreate`). For example:

```
txc workspace component create pp-form-tab --output <project-dir> --param FormId=<form-guid> --param FormType=main --param EntitySchemaName=<prefix_table>
```

### ControlType values for pp-form-control
`Text`, `MultilineText`, `WholeNumber`, `Decimal`, `Float`, `Currency`, `DateTime`, `Lookup`, `OptionSet`, `SubGrid`, `Button`

### Dialog-specific templates
- **`pp-form-dialog-tabfooter`** — Add a footer area to a dialog tab
- **`pp-form-event-handler`** — Register a JavaScript event (onload, onsave, onchange, onclick)

## Control ClassId Reference

Each form control type maps to a ClassId GUID:

| ControlType | ClassId |
|---|---|
| Text | `{4273EDBD-AC1D-40d3-9FB2-095C621B552D}` |
| MultilineText | `{E0DECE4B-6FC8-4a8f-A065-082708572369}` |
| WholeNumber | `{C3EFE0C3-0EC6-42be-8349-CBD9079DFD8E}` |
| Decimal/Float | `{C3EFE0C3-0EC6-42be-8349-CBD9079DFD8E}` |
| Currency | `{533B9E00-756B-4312-95A0-DC888637AC78}` |
| DateTime | `{5B773807-9FB2-42db-97C3-7A91EFF8ADFF}` |
| Lookup | `{270BD3DB-D9AF-4782-9025-509E298DEC0A}` |
| OptionSet | `{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}` |
| Boolean | `{67FAC785-CD58-4f9f-ABB3-4B7DDC6ED5ED}` |
| SubGrid | `{E7A81278-8635-4d9e-8D4D-59480B391C5B}` |
| Button | `{00AD73DA-BD4D-49C6-88A8-2F4F4CAD4A20}` |

### Form Type Codes
- `2` = Main form
- `7` = Quick Create form
- `6` = Quick View form
- `11` = Card form

### View querytype Values
- `0` = Public (standard)
- `1` = Advanced Find
- `2` = Associated
- `4` = Quick Find
- `64` = Saved Query (lookup view)

See also: [component-creation](component-creation.md), [schema-management](schema-management.md)
