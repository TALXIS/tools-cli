# Business Process Flow Development

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## Key Concept

Business Process Flows (BPFs) guide users through stages and steps on a record form. BPFs are a special entity type (`IsBPFEntity=1`) that combine entity creation, XAML workflow authoring, and relationship configuration.

## BPF Scaffolding Chain

BPF creation follows a strict 3-step chain. **Order matters.**

1. **Create BPF Entity** → `txc workspace component create pp-bpf --output <project-dir> --param PublisherPrefix=<prefix> --param BPFName=<name> --param PrimaryEntity=<entity>`
2. **Add Stages** → `txc workspace component create pp-bpf-stage --output <project-dir> --param BPFLogicalName=<bpf-logical-name> --param StageName=<stage> ...`
3. **Add Steps to Stages** → `txc workspace component create pp-bpf-stage-step --output <project-dir> --param BPFLogicalName=<bpf-logical-name> --param StageName=<stage> ...`

`<project-dir>` is the folder of the solution project that owns the BPF (find it in the repository, for example with `dotnet sln list`). `<prefix>` is the customization prefix of the target solution (see [component-creation](component-creation.md)). The templates themselves mark only `PublisherPrefix` and `BPFName` (`pp-bpf`) and `BPFLogicalName` (`pp-bpf-stage`, `pp-bpf-stage-step`) as required, but in practice also pass `PrimaryEntity` and `StageName` as shown, or placeholders can remain in the generated files. Run `txc workspace component parameter list <template>` (for example `pp-bpf-stage`) for the full, current list at each step.

## Architecture Decisions

- **Simple Linear BPF** — single entity, sequential stages, no branching (approval, onboarding)
- **Cross-Entity BPF** — spans multiple tables (e.g., Lead → Opportunity → Quote); requires relationships between entities
- **BPF with Branching** — conditional paths based on field values (e.g., customer tier routing)

## Relationship to Primary Entity

- The BPF entity has a many-to-one relationship to the primary entity
- Cross-entity BPFs: each stage specifies which entity it operates on
- Ensure all referenced entities and attributes exist before scaffolding

## What NOT to Do

- ❌ Don't create stages before the BPF entity — the XAML workflow must exist first
- ❌ Don't forget `IsBPFEntity=1` — without it, Dataverse treats it as a regular entity
- ❌ Don't reference attributes that don't exist on the stage's entity
- ❌ Don't create circular branch paths — BPFs must have a clear forward progression
- ❌ Don't skip the `TriggerOnCreate=1` setting if the BPF should auto-start

See also: [component-creation](component-creation.md), [form-xml-reference](form-xml-reference.md)
