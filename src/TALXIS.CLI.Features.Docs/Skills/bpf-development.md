# Business Process Flow Development

## Key Concept

Business Process Flows (BPFs) guide users through a defined business process by presenting stages and steps on a record form. BPFs are a special entity type (`IsBPFEntity=1`) that combine entity creation, XAML workflow authoring, and relationship configuration.

## BPF Entity Structure

A BPF is a Dataverse entity with special attributes that track process state:

| Attribute | Type | Description |
|---|---|---|
| `ActiveStageId` | Lookup | Currently active stage in the process |
| `ActiveStageStartedOn` | DateTime | When the current stage was activated |
| `Duration` | WholeNumber | Total time spent in the process (minutes) |
| `ProcessId` | Lookup | Reference to the workflow definition |
| `TraversedPath` | String | Comma-separated list of traversed stage IDs |

The BPF entity definition uses `IsBPFEntity=1` in its `Entity.xml`, which tells Dataverse to treat it as a process entity rather than a data entity.

## Workflow Metadata

BPF workflows have specific metadata properties:

| Property | Value | Description |
|---|---|---|
| `Category` | 4 | Identifies this as a BPF workflow (vs. other workflow types) |
| `TriggerOnCreate` | 1 | Automatically starts when the primary record is created |
| `Type` | 1 | Definition workflow (not activated instance) |

## Stage Configuration

Each stage represents a phase of the business process:

```
BPF Workflow (XAML)
├─ Stage 1: "Qualify"
│  ├─ Step: Company Name (required)
│  └─ Step: Budget Amount (optional)
├─ Stage 2: "Develop"
│  ├─ Step: Stakeholder Identified (required)
│  └─ Step: Proposal Created (required)
├─ Stage 3: "Propose"
│  └─ Step: Proposal Reviewed (required)
└─ Stage 4: "Close"
   └─ Step: Final Decision (required)
```

### Stage Properties
- **Stage Name** — displayed in the BPF header
- **Stage Category** — groups stages logically (Qualify, Develop, Propose, Close)
- **Entity** — which table this stage operates on (supports cross-entity BPFs)

### Step Properties
- **Attribute** — the field the user must complete at this step
- **IsRequired** — whether the step must be completed before advancing (`0` or `1`)
- **Sequence** — order of steps within the stage

## Branching Logic

BPFs support conditional branching where the next stage depends on field values:

```
Stage: "Qualify"
  └─ Branch condition: Budget > $100,000
     ├─ TRUE  → Stage: "Enterprise Develop"
     └─ FALSE → Stage: "Standard Develop"
```

Branching is defined in the XAML workflow with condition elements that evaluate field values and route to different stage sequences.

## BPF Scaffolding Chain

BPF creation follows a strict 3-step chain:

### Step 1: Create BPF Entity
```
Tool: workspace_component_create
Parameters: { componentType: "BPF", SolutionRootPath: "Declarations", ... }
```
Creates the BPF entity with special attributes, the XAML workflow definition, and workflow metadata.

### Step 2: Add Stages
```
Tool: workspace_component_create
Parameters: { componentType: "BPFStage", SolutionRootPath: "Declarations", ... }
```
Adds stage definitions to the XAML workflow. Each stage specifies name, category, and target entity.

### Step 3: Add Steps to Stages
```
Tool: workspace_component_create
Parameters: { componentType: "BPFStageStep", SolutionRootPath: "Declarations", ... }
```
Adds steps within a stage, binding each step to a specific attribute with required/optional flag.

## Relationship to Primary Entity

A BPF is always associated with one or more primary entities:
- The BPF entity has a many-to-one relationship to the primary entity
- Cross-entity BPFs can span multiple tables (e.g., Lead → Opportunity → Quote)
- Each stage specifies which entity it operates on

## Common BPF Patterns

### Simple Linear BPF
Single entity, sequential stages, no branching:
- Best for straightforward processes (e.g., approval workflow, onboarding)
- Each stage has 1–5 required fields

### Cross-Entity BPF
Spans multiple tables through the process:
- Lead qualification → Opportunity development → Quote creation
- Each stage binds to a different entity
- Requires relationships between the entities

### BPF with Branching
Conditional paths based on data:
- Different tracks for different customer tiers
- Compliance-dependent process variations

## What NOT to Do

- ❌ Don't create stages before the BPF entity — the XAML workflow must exist first
- ❌ Don't forget `IsBPFEntity=1` — without it, Dataverse treats it as a regular entity
- ❌ Don't reference attributes that don't exist on the stage's entity
- ❌ Don't create circular branch paths — BPFs must have a clear forward progression
- ❌ Don't skip the `TriggerOnCreate=1` setting if the BPF should auto-start

See also: [component-creation](component-creation.md), [form-xml-reference](form-xml-reference.md)
