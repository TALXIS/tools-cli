# PCF Control Development

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). If the TALXIS MCP server is connected instead, every command is also a tool named by joining its words with `_` (`txc workspace component create` is the tool `workspace_component_create`). The tool list is generated from the CLI commands, so each tool takes the arguments and options of its command, and the CLI help is the reference on either route.

## Key Concept

Power Apps Component Framework (PCF) controls are custom TypeScript UI components for model-driven and canvas apps. They have their own project structure, manifest, and build chain — distinct from standard Dataverse XML scaffolding.

## When to Use PCF

- Custom field rendering beyond out-of-box controls
- Custom grid/list displays (dataset controls)
- Complex interactive UI that can't be achieved with standard forms

## Project Structure

- Convention: `Controls.{Domain}/` in `src/` with `ProjectType=Pcf` in `.csproj`
- Each control has: `ControlManifest.Input.xml` (definition), `index.ts` (implementation), optional CSS
- Uses `pcf-scripts` for build tooling

## Control Type Decision

- **Field control** (`control-type="standard"`) — binds to a single field value
- **Dataset control** (`control-type="dataset"`) — binds to a view/subgrid, for custom grids or lists

## Scaffolding and Build

1. Scaffold with `workspace_component_create` using `componentType: "pp-pcf"`
2. Call `workspace_component_parameter_list` for required parameters
3. Local dev: `npm run build` and `npm start watch` for test harness
4. Solution packaging includes the control bundle automatically during `pack`

## What NOT to Do

- ❌ Don't manipulate DOM outside the provided `container` element
- ❌ Don't use `document.getElementById` — use the container reference instead
- ❌ Don't forget to call `notifyOutputChanged()` when the value changes
- ❌ Don't skip `destroy()` cleanup — it causes memory leaks
- ❌ Don't mix dataset and field control types — choose one per control

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
