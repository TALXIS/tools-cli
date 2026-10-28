# Business Process Flow Development

Business Process Flows (BPFs) guide users through stages and steps on a record form. A BPF is a special entity (`IsBPFEntity=1`; without it Dataverse treats it as a regular entity) combining entity creation, XAML workflow and relationships.

## BPF Scaffolding Chain

Strict order; each step needs the previous one:

1. `txc workspace component create pp-bpf --output <project-dir> --param PublisherPrefix=<prefix> --param BPFName=<name> --param PrimaryEntity=<entity>`
2. `txc workspace component create pp-bpf-stage --output <project-dir> --param BPFLogicalName=<bpf-logical-name> --param StageName=<stage> ...`
3. `txc workspace component create pp-bpf-stage-step --output <project-dir> --param BPFLogicalName=<bpf-logical-name> --param StageName=<stage> ...`

`<project-dir>` is the solution project that owns the BPF; `<prefix>` is its customization prefix ([component-creation](component-creation.md)). Templates mark only `PublisherPrefix`, `BPFName` (`pp-bpf`) and `BPFLogicalName` (stage templates) required, but also pass `PrimaryEntity` and `StageName`, or placeholders can remain in the generated files. `txc workspace component parameter list <template>` shows the rest.

## Design

- **Linear** — one entity, sequential stages (approval, onboarding)
- **Cross-entity** — spans tables (Lead → Opportunity → Quote); each stage names its entity and the entities need relationships
- **Branching** — conditional paths on field values; keep a clear forward progression, no circular branches

The BPF entity has a many-to-one relationship to the primary entity. Referenced entities and attributes must exist before scaffolding. Set `TriggerOnCreate=1` if the BPF should auto-start.

See also: [component-creation](component-creation.md), [form-xml-reference](form-xml-reference.md)
