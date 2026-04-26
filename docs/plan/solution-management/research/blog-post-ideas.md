# Blog Post Ideas

Opportunities for blog posts based on our research and discoveries during the solution management feature design.

## 1. "How Dataverse Smart Diff Actually Works — A Deep Dive into the Server-Side Patch Generator"

**Source:** Decompiled `GraphComparer.cs`, `FileComparer.cs`, `XMLConverter.cs`

**Content:**
- The GraphComparer algorithm: XML → XPath graph → property-by-property comparison
- How binary files (WebResources, plugins) are compared with `SequenceEqual`
- Label comparison: only base LCID is compared, structured OptionSet XML comparison
- `EntityMinimumSetCalculator`: how Dataverse decides full vs minimal serialization
- Which component types support smart diff (gated by feature flags)
- The `ReserveComponent` pattern: comparing against a template entity to generate minimal diffs for new entities
- `OverwriteUnmanagedCustomizations` flag's effect on smart diff skip logic

**Unique angle:** Nobody has documented the internal algorithm before. The `SmartDiffEnabledComponentListProvider` shows exactly which types are supported.

---

## 2. "Inside Dataverse Git Integration — How the Native Source Control Sync Works"

**Source:** Decompiled `IGitHelper`, `SourceControlSolutionPackager`, `*Packer.cs` files

**Content:**
- The `IGitHelper` abstraction (GitHub + ADO implementations)
- Change detection: `githashid` is literally the Git blob SHA-1 from ADO API (Dataverse doesn't compute hashes)
- The `DetermineAction()` four-state matrix (None/Push/Pull/Conflict)
- 15+ `IComponentPacker` implementations and how they serialize different component types
- File format: XML default, YAML opt-in, canonical form always XML
- The `iscommitted` flag lifecycle
- `sourcecontrolcomponent` and `sourcecontrolcomponentpayload` entity schema
- Why it requires a live ADO instance and how a CLI could bypass that

**Unique angle:** First public documentation of the Git integration internals. Explains why certain limitations exist.

---

## 3. "Dataverse Solution Layering Internals — The State Machine Behind Component Layers"

**Source:** Decompiled `SolutionAwareComponents/*`, `ComponentInstance.cs`, `LayerAction.cs`

**Content:**
- The `BusinessComponentState` layer stack: Base, Top, Published, Snapshot
- `ComponentState` enum: Publish, Unpublish, Delete, UnpublishedDelete, Snapshot, Stage
- `LayerMatchRule`: how Dataverse decides where to insert a new layer (Self, Patch, Holding, BelowActive, Publisher, PublisherGroup)
- `MergeComponentInstanceAction`: how properties propagate across layers during managed solution install
- Why `RemoveActiveCustomizations` can't work when the unmanaged layer is the only layer
- How `OverwriteTime` is used

**Unique angle:** Explains the state machine that puzzles ALM practitioners.

---

## 4. "The 119 Component Types of Dataverse — What ComponentDefinition Really Tells You"

**Source:** Decompiled `ComponentDefinitionXml.cs`, blog post on SCF

**Content:**
- The `ComponentDefinition` class: 40+ properties that define each type
- Platform types (119 hardcoded) vs SCF types (dynamic, runtime-assigned)
- The `ComponentXPath` field: how components are located in solution XML
- `HasParent` / `RootComponent` / `GroupParent`: the component hierarchy
- `rootcomponentbehavior`: 0=include all, 1=shell only, 2=no metadata
- `DependencyCalculatorClass`: each type has its own dependency calculator
- `RemoveActiveCustomizationsBehavior`: None, NoCascade, Cascade
- How `solutioncomponentdefinitions` API reveals SCF types at runtime

**Unique angle:** Combines decompiled server data with the public API, giving practitioners a complete picture.

---

## 5. "Building a Client-Side Solution Diff for Dataverse — Fast Inner Loop for Power Platform Developers"

**Source:** Our Phase 4 sync design + server source analysis

**Content:**
- The problem: full solution imports are slow for small changes
- How the server-side Smart Diff works (summary of blog #1)
- Porting `GraphComparer` to a CLI tool
- Using `msdyn_componentlayer` API to get server-side component definitions
- Computing content hashes for fast change detection
- Building minimal patch solution ZIPs
- SCF compatibility challenges (runtime type codes, no static schema)
- Benchmark results: full import vs patch import

**Unique angle:** Practical how-to for the community, with real performance numbers.

---

## 6. "Undocumented Dataverse APIs You Should Know About"

**Source:** HAR analysis of make.powerapps.com

**Content:**
- `msdyn_solutioncomponentsummaries` — the portal's primary component listing API (71 calls in one session!)
- `msdyn_solutioncomponentcountsummaries` — quick per-type counts
- `RetrieveDependenciesForUninstall(SolutionUniqueName=...)` — pre-uninstall safety check
- `RetrieveUnpublishedMultiple()` — view unpublished draft forms/views
- `GetOrgDbOrgSetting` for `IsLockdownOfUnmanagedCustomizationEnabled`
- `solutioncomponentdefinitions` — discover all registered component types
- The `mscrm.solutionuniquename` and `mscrm.mergelabels` headers
- `WithMetadata` dependency variants (work in portal, return empty via API — why?)

**Unique angle:** Reverse-engineered from the actual portal, validated with live tests.

---

## 7. "How Dataverse Computes Component Hashes (Hint: It Doesn't)"

**Source:** Decompiled `SourceControlIntegrationHelper.cs`, `AdoGitIntegrationClient.cs`

**Content:**
- Common misconception: Dataverse computes content hashes for Git integration
- Reality: `githashid` = Azure DevOps Git blob `objectId` (standard Git SHA-1)
- The `DetermineAction()` algorithm using `iscommitted` + hash comparison
- `ChecksumGenerator.cs` uses SHA-512 but is NOT used in the Git diff pipeline
- `FileChecksumUtility.cs` exists but is for data integrity, not sync
- Implications for building your own sync tool without ADO
- How to compute equivalent hashes from local files

**Unique angle:** Clears up a common misconception, provides practical guidance.
