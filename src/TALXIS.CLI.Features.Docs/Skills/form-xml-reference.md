# Form XML Structure Reference

Scaffold forms with the `pp-form-*` templates instead of writing XML by hand: they generate ClassIDs, hierarchy and required attributes, and the build validates the result. `<project-dir>` is the folder of the solution project that owns the form.

## Form XML Hierarchy

Strict 6-level nesting: **Tab → Column → Section → Row → Cell → Control**. Skipping a level produces invalid XML. `<labels>` elements are required for localization.

## Form Types

- **Main** — default editing form, multiple tabs, event handlers, business rules
- **Dialog** — popup, single tab, `<tabfooter>` for action buttons
- **Quick Create** — compact creation, single tab, essential fields
- **Quick View** — read-only, shown inside another form's lookup field

Customizations add XML fragments at insertion points (parent element by name/ID plus position) rather than replacing the whole form.

## Scaffolding Chain

Scaffold the Entity and Attributes first, then build top-down, one template call per level:

1. **`pp-entity-form`** — the form: `txc workspace component create pp-entity-form --output <project-dir> --param FormType=main --param EntitySchemaName=<prefix_table> --param FormId=<form-guid>` (`FormType` is `main`, `dialog` or `quickCreate`; without `FormId` a random id is generated and printed)
2. **`pp-form-tab`** — a tab (`RemoveDefaultTab=True` replaces the empty default)
3. **`pp-form-column`** — inside the tab
4. **`pp-form-section`** — inside the column
5. **`pp-form-row`** — inside the section (one per field)
6. **`pp-form-cell`** — inside the row (the label)
7. **`pp-form-control`** — inside the cell (the field)

Templates `pp-form-tab` to `pp-form-control` find the form through `FormId`, `FormType` and `EntitySchemaName`. Their defaults are placeholders, so pass all three: the `FormId` given to `pp-entity-form` (generate a GUID up front or read it from the form file), the same `EntitySchemaName`, and `FormType` `main`, `quick` or `dialog` (`quick`, where `pp-entity-form` says `quickCreate`).

Also: `pp-form-dialog-tabfooter` (footer of a dialog tab), `pp-form-event-handler` (JavaScript event: onload, onsave, onchange, onclick). `ControlType` values for `pp-form-control`: `Text`, `MultilineText`, `WholeNumber`, `Decimal`, `Float`, `Currency`, `DateTime`, `Lookup`, `OptionSet`, `SubGrid`, `Button`. Other parameters: `txc workspace component parameter list <template>`.

## Control ClassId Reference

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

Form type codes: `2` Main, `7` Quick Create, `6` Quick View, `11` Card.

View `querytype`: `0` Public, `1` Advanced Find, `2` Associated, `4` Quick Find, `64` Saved Query (lookup view).

See also: [component-creation](component-creation.md), [schema-management](schema-management.md)
