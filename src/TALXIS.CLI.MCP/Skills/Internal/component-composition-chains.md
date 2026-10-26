# Component Composition Chains

<!-- Internal reasoning skill: ordering constraints for multi-step component creation. -->
<!-- Use workspace_component_parameter_list to discover parameters for each component type. -->

## Table Chain
1. Entity → 2. Attributes (repeat) → 3. Forms → 4. Views → 5. Optional: App/Sitemap/Role
- NEVER create forms before attributes — controls would reference non-existent fields
- ALWAYS use workspace tools, not environment tools

## Plugin Chain
1. PluginProject → 2. Write plugin class, declare steps with [PluginStep] → 3. Reference the project from the Logic solution → 4. Build the solution (registers the assembly, generates the steps) → 5. Optional: PluginTest
- PluginAssembly is NOT scaffolded by default: the solution build registers it (GeneratePluginAssembly=true). pp-plugin-assembly after a build adds a second root component and breaks the next build
- Manual PluginStep (pp-plugin-assembly-step) only for what attributes cannot express; the assembly registration must exist first
- See plugin-development skill for stage selection, attribute settings and message ids

## Form Modification
1. workspace_explain → locate form XML
2. Read form XML → find insertion point
3. IF field → add row/cell/control in target section (see form-xml-reference skill for ClassIDs)
   IF section → add section in target column with labels and GUID
   IF tab → add tab with columns/sections, set IsUserDefined="1"
4. Validate: no skipped hierarchy levels, all datafieldnames exist, all GUIDs unique

## Custom API Chain
1. Create backing plugin FIRST (Plugin Chain above) → 2. CustomAPI → 3. RequestParameters (repeat) → 4. ResponseProperties (repeat)
- Plugin MUST exist before API definition — plugintypeid references it
- Naming: {prefix}_{apiname}, params: {prefix}_{apiname}.{ParamName}

## BPF Chain
1. BPF entity → 2. BPFStage (repeat) → 3. BPFStageStep (repeat per stage)
- Entity must have IsBPFEntity=1; XAML workflow needs Category=4, TriggerOnCreate=1

## Local vs Live
- ALL chains above: use workspace tools (local, instant, reversible)
- Environment tools ONLY for: inspection, layer troubleshooting, import, publish
