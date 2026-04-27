# PCF Control Development

## Key Concept

Power Apps Component Framework (PCF) controls are custom UI components built with TypeScript that extend model-driven and canvas apps. PCF controls have their own project structure, manifest, lifecycle methods, and build chain — distinct from standard Dataverse XML scaffolding.

## PCF Project Structure

```
src/
└─ Controls.{Domain}/
   ├─ Controls.{Domain}.csproj     # ProjectType=Pcf
   ├─ pcfconfig.json               # Build output configuration
   └─ {ControlName}/
      ├─ ControlManifest.Input.xml # Control definition and properties
      ├─ index.ts                  # TypeScript control implementation
      └─ css/
         └─ {ControlName}.css      # Optional styling
```

**Project file essentials (.csproj):**
- `ProjectType=Pcf` — tells the build SDK this is a PCF project
- Uses `pcf-scripts` for build tooling
- Produces a control bundle for packaging

**Build configuration (`pcfconfig.json`):**
```json
{
  "outDir": "./out/controls"
}
```

## Control Manifest

The `ControlManifest.Input.xml` defines the control's metadata, properties, and resources:

```xml
<manifest>
  <control namespace="TALXIS.Controls"
           constructor="MyControl"
           version="1.0.0"
           display-name-key="MyControl"
           description-key="MyControl_Desc"
           control-type="standard">
    <property name="value"
              display-name-key="Value"
              of-type="SingleLine.Text"
              usage="bound"
              required="true" />
    <resources>
      <code path="index.ts" order="1" />
      <css path="css/MyControl.css" order="1" />
    </resources>
  </control>
</manifest>
```

## Property Types

| Type | Description | Example Use |
|---|---|---|
| `SingleLine.Text` | Single-line text | Name, email, URL fields |
| `Multiple` | Multi-line text | Description, notes |
| `WholeNone` | Integer (no format) | Count, quantity |
| `Decimal` | Decimal number | Percentage, rating |
| `FP` | Floating-point | Scientific values |
| `Currency` | Money value | Price, cost |
| `DateAndTime.DateOnly` | Date only | Birth date |
| `DateAndTime.DateAndTime` | Date and time | Appointment, deadline |
| `TwoOptions` | Boolean | Yes/No toggle |
| `OptionSet` | Choice/picklist | Status, category |
| `Lookup.Simple` | Lookup reference | Related record |

### Property Usage
- `usage="bound"` — bound to a Dataverse field (read/write)
- `usage="input"` — configuration-only input (read-only from field perspective)

## Lifecycle Methods

PCF controls implement the `StandardControl` interface with four lifecycle methods:

### `init(context, notifyOutputChanged, state, container)`
Called once when the control is loaded. Set up DOM, event listeners, and initial state.
```typescript
public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
): void {
    this._container = container;
    this._notifyOutputChanged = notifyOutputChanged;
    // Build initial DOM
}
```

### `updateView(context)`
Called whenever bound data changes or the form resizes. Update the DOM to reflect new values.
```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
    const value = context.parameters.value.raw;
    // Update DOM with new value
}
```

### `getOutputs()`
Called when the framework needs the control's current output values (after `notifyOutputChanged()`).
```typescript
public getOutputs(): IOutputs {
    return {
        value: this._currentValue
    };
}
```

### `destroy()`
Called when the control is removed from the DOM. Clean up event listeners and resources.
```typescript
public destroy(): void {
    // Remove event listeners, cancel pending operations
}
```

## Dataset vs Field Controls

### Field Controls (`control-type="standard"`)
Bind to a single field value. Used for custom input/display of individual fields.

### Dataset Controls (`control-type="dataset"`)
Bind to a dataset (view/subgrid). Used for custom grids, charts, or list displays.

```xml
<control namespace="TALXIS.Controls" constructor="CustomGrid"
         control-type="dataset">
  <data-set name="records" display-name-key="Records">
    <property-set name="name" display-name-key="Name"
                  of-type="SingleLine.Text" usage="bound" />
  </data-set>
</control>
```

## Building and Packaging

### Local Development
```bash
# Build the control
cd src/Controls.{Domain}
npm run build

# Start test harness for local debugging
npm start watch
```

### Production Build
The `txc` workspace tools handle building PCF controls as part of the solution pack workflow:
```
Tool: workspace_component_create
Parameters: { componentType: "PCFControl", SolutionRootPath: "Declarations", ... }
```

### Solution Packaging
PCF controls are packaged into solution ZIP files for deployment. The build SDK automatically includes the control bundle in the solution package during `pack`.

## What NOT to Do

- ❌ Don't manipulate DOM outside the provided `container` element
- ❌ Don't use `document.getElementById` — use the container reference instead
- ❌ Don't forget to call `notifyOutputChanged()` when the value changes
- ❌ Don't skip `destroy()` cleanup — it causes memory leaks
- ❌ Don't mix dataset and field control types — choose one per control

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
