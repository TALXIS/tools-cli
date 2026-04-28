# Business Process Flow Development

## Key Concept

Business Process Flows (BPFs) guide users through stages and steps on a record form. BPFs are a special entity type (`IsBPFEntity=1`) that combine entity creation, XAML workflow authoring, and relationship configuration.

## BPF Scaffolding Chain

BPF creation follows a strict 3-step chain. **Order matters.**

1. **Create BPF Entity** → `workspace_component_create` with `componentType: "pp-bpf"`
2. **Add Stages** → `workspace_component_create` with `componentType: "pp-bpf-stage"`
3. **Add Steps to Stages** → `workspace_component_create` with `componentType: "pp-bpf-stage-step"`

Call `workspace_component_parameter_list` for required parameters at each step.

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
