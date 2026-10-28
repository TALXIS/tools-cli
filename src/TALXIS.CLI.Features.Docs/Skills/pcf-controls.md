# PCF Control Development

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## Key Concept

Power Apps Component Framework (PCF) controls are custom TypeScript UI components for model-driven and canvas apps. They have their own project structure, manifest, and build chain — distinct from standard Dataverse XML scaffolding.

## When to Use PCF

- Custom field rendering beyond out-of-box controls
- Custom grid/list displays (dataset controls)
- Complex interactive UI that can't be achieved with standard forms

## Project Structure

- Default layout (the user's request and the repository's existing layout come first): `Controls.{Domain}/` in `src/` with `ProjectType=Pcf` in `.csproj`
- Each control has: `ControlManifest.Input.xml` (definition), `index.ts` (implementation), optional CSS
- Uses `pcf-scripts` for build tooling

## Control Type Decision

- **Field control** (`control-type="standard"`) — binds to a single field value
- **Dataset control** (`control-type="dataset"`) — binds to a view/subgrid, for custom grids or lists

## Scaffolding and Build

1. Scaffold with `txc workspace component create pp-pcf --output <control-project-dir> --param Name=<ControlName> --param Namespace=<Namespace>` (`--output` is the folder the component is scaffolded into: for `pp-pcf`, the control project folder; by default `src/Controls.<Domain>`, unless the repository already has a controls project or layout)
2. Run `txc workspace component parameter list pp-pcf` for the full, current list of parameters
3. Local dev: `npm run build` and `npm start watch` for test harness
4. Solution packaging includes the control bundle automatically during `pack`

## What NOT to Do

- ❌ Don't manipulate DOM outside the provided `container` element
- ❌ Don't use `document.getElementById` — use the container reference instead
- ❌ Don't forget to call `notifyOutputChanged()` when the value changes
- ❌ Don't skip `destroy()` cleanup — it causes memory leaks
- ❌ Don't mix dataset and field control types — choose one per control

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
