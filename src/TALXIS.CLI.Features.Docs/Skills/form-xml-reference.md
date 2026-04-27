# Form XML Structure Reference

## Key Concept

Dataverse model-driven app forms are defined in XML with a strict 6-level hierarchy. Form modifications add XML **fragments** at specific positions rather than replacing whole forms. Understanding this hierarchy is essential for correct form scaffolding and customization.

## Form XML Hierarchy

```
Form
└─ Tab
   └─ Column
      └─ Section
         └─ Row
            └─ Cell
               └─ Control
```

### Tab
Top-level container — groups related columns into a visual tab.
```xml
<tab verticallayout="true" id="{guid}" IsUserDefined="1" name="tab_general">
  <labels>
    <label description="General" languagecode="1033" />
  </labels>
  <columns>
    <!-- Columns go here -->
  </columns>
</tab>
```

### Column
Width-based layout container within a tab. Percentage widths must total 100%.
```xml
<column width="100%">
  <sections>
    <!-- Sections go here -->
  </sections>
</column>
```

Two-column layout:
```xml
<column width="50%"><sections>...</sections></column>
<column width="50%"><sections>...</sections></column>
```

### Section
Groups related fields with an optional label and bar.
```xml
<section showlabel="true" showbar="false" IsUserDefined="0"
         id="{guid}" name="section_details">
  <labels>
    <label description="Details" languagecode="1033" />
  </labels>
  <rows>
    <!-- Rows go here -->
  </rows>
</section>
```

### Row
Container for cells. Typically one row per field.
```xml
<row>
  <!-- Cells go here -->
</row>
```

### Cell
Wraps a control with optional label. Contains the actual field binding.
```xml
<cell id="{guid}" labelid="{guid}">
  <labels>
    <label description="Email Address" languagecode="1033" />
  </labels>
  <control classid="{classid}" datafieldname="emailaddress1" id="emailaddress1" />
</cell>
```

### Control
The actual field binding with a ClassID determining the control type.
```xml
<control classid="{4273EDBD-AC1D-40D3-9FB2-095C621B552D}"
         datafieldname="emailaddress1" id="emailaddress1" />
```

## Control ClassID Reference

This table maps control types to their Dataverse ClassIDs. **Use the correct ClassID** — incorrect values cause form rendering errors.

| Control Type | ClassID | Notes |
|---|---|---|
| Text / MultilineText / Memo | `{4273EDBD-AC1D-40D3-9FB2-095C621B552D}` | Single-line text, multi-line, memo fields |
| WholeNumber | `{C6D124CA-7EDA-4A60-AEA9-7FB8D318B68F}` | Integer fields |
| Decimal / Float | `{C3EFE0C3-0EC6-42BE-8349-CBD9079DFD8E}` | Decimal and floating-point fields |
| Currency | `{533B9E00-756B-4312-95A0-DC888637AC78}` | Money fields |
| DateTime | `{5B773807-9FB2-42DB-97C3-7A91EFF8ADFF}` | Date and date-time fields |
| Lookup | `{270BD3DB-D9AF-4782-9025-509E298DEC0A}` | Lookup/reference fields |
| OptionSet | `{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}` | Choice/picklist fields |
| MultiSelect OptionSet | `{4AA28AB7-9C13-4F57-A73D-AD894D048B5F}` | Multi-select choice fields |
| TwoOptions (Boolean) | `{67FAC785-CD58-4F9F-ABB3-4B7DDC6ED5ED}` | Yes/No toggle fields |
| SubGrid | `{E7A81278-8635-4D9E-8D4D-59480B391C5B}` | Related record grids |
| Button | `{00AD73DA-BD4D-49C6-88A8-2F4F4CAD4A20}` | Command bar buttons on forms |
| IFrame | `{FD2A7985-3187-444E-908D-6624B21F69C0}` | Embedded web pages |
| Web Resource | `{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}` | Embedded HTML/JS resources |
| Notes | `{06375649-C143-495E-A496-C962E5B4488E}` | Timeline/notes control |
| Timer | `{9C5CA0A1-AB4D-4781-BE7E-8DFBE867B87E}` | SLA timer control |

## Form Types

### Main Form
The default editing form for a table. Contains the full Tab→Column→Section→Row→Cell→Control hierarchy. Supports multiple tabs, event handlers, and business rules.

### Dialog Form
Used in dialog/popup contexts. Key differences from main form:
- `shownavigationbar="false"` on the form element
- Contains `<tabfooter>` sections for dialog action buttons
- Cells use `showlabel="true"` attribute and `uniqueid` attribute
- Simpler layout — typically single tab, single column

### Quick Create Form
Compact form for inline record creation. Subset of main form structure:
- Single tab, limited sections
- No navigation bar
- Only essential fields

### Quick View Form
Read-only form displayed inside another form's lookup field:
- Shows related record details inline
- Cannot contain editable controls
- Referenced via the lookup control configuration

## Form Fragments

Form modifications add XML fragments at specific insertion points rather than replacing the entire form. This is the standard pattern for customization:

```
Base form (managed solution)
  ├─ Fragment: Add tab at position N
  ├─ Fragment: Add section to existing tab
  ├─ Fragment: Add cell to existing section
  └─ Fragment: Add event handler
```

Each fragment targets a specific parent element by name/ID and specifies its position relative to existing children.

## SubGrid Configuration

SubGrid controls display related records within a form:
```xml
<cell id="{guid}">
  <labels>
    <label description="Related Contacts" languagecode="1033" />
  </labels>
  <control classid="{E7A81278-8635-4D9E-8D4D-59480B391C5B}" id="subgrid_contacts">
    <parameters>
      <TargetEntityType>contact</TargetEntityType>
      <ViewId>{view-guid}</ViewId>
      <EnableViewPicker>true</EnableViewPicker>
      <RelationshipName>account_contacts</RelationshipName>
    </parameters>
  </control>
</cell>
```

## Common Scenarios

### Adding a Field to an Existing Section
1. Identify the target section by name/ID in the form XML
2. Add a `<row>` with a `<cell>` containing the `<control>` element
3. Use the correct ClassID from the reference table above
4. Set `datafieldname` to the logical attribute name

### Adding a New Tab
1. Add a `<tab>` element to the form's tab collection
2. Include at least one `<column>` with one `<section>`
3. Set `IsUserDefined="1"` for custom tabs

### Adding a SubGrid
1. Add a section for the subgrid (subgrids need their own section)
2. Use SubGrid ClassID `{E7A81278-8635-4D9E-8D4D-59480B391C5B}`
3. Configure `<parameters>` with target entity, view, and relationship

## What NOT to Do

- ❌ Don't guess ClassIDs — use the reference table above
- ❌ Don't skip hierarchy levels (e.g., control directly inside tab without section/row/cell)
- ❌ Don't mix up main form and dialog form attributes
- ❌ Don't forget `<labels>` elements — they're required for localization

See also: [component-creation](component-creation.md), [schema-management](schema-management.md)
