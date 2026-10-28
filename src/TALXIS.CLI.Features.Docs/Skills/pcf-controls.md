# PCF Control Development

Power Apps Component Framework (PCF) controls are custom TypeScript UI components for model-driven and canvas apps, with their own manifest and build chain, separate from Dataverse XML scaffolding. Use them for field rendering, custom grids or interactive UI that standard forms cannot do.

## Project Structure

- Default: `src/Controls.{Domain}/` with `ProjectType=Pcf` in the `.csproj`.
- Each control has `ControlManifest.Input.xml` (definition), `index.ts` (implementation), optional CSS; build tooling is `pcf-scripts`.
- Choose one type per control: **field** (`control-type="standard"`, one field value) or **dataset** (`control-type="dataset"`, a view/subgrid).

## Scaffolding and Build

1. `txc workspace component create pp-pcf --output <control-project-dir> --param Name=<ControlName> --param Namespace=<Namespace>` (`--output` is the control project folder; use an existing controls project if the repository has one). Other parameters: `txc workspace component parameter list pp-pcf`.
2. Local dev: `npm run build`, `npm start watch` for the test harness.
3. Solution `pack` includes the control bundle automatically.

## What NOT to Do

- ❌ Don't touch the DOM outside the provided `container` (no `document.getElementById`)
- ❌ Don't forget `notifyOutputChanged()` when the value changes, or `destroy()` cleanup (memory leaks)

See also: [component-creation](component-creation.md), [project-structure](project-structure.md)
