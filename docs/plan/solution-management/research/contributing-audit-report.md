# Solution Management Plan — Contributing Rules Audit

Audit of `docs/plan/solution-management/` against `CONTRIBUTING.md`, `docs/output-contract.md`, `docs/architecture.md`, `src/Directory.Build.props`, `src/BannedSymbols.txt`, and related governance files.

---

## ✅ Rules the Plan Follows Correctly

### Command Surface & Taxonomy
- ✅ **Top-level taxonomy respected.** All new commands live under the existing `environment` group — no new top-level groups introduced.
- ✅ **Nouns, not platforms.** No platform name (`dataverse`, `azure`) appears in any command path. Commands use `solution`, `component`, `layer`, `dependency`.
- ✅ **Verb vocabulary.** Uses `list`, `get`, `create`, `publish`, `export` — all from the approved verb set. `uninstall-check` and `delete-check` are compound verbs for safety checks, consistent with the CLI's diagnostic pattern.
- ✅ **`list` vs `show`/`get` split.** `sln list` (many, brief) vs `sln get` (one, detailed). Same for `comp layer list` vs `comp layer get`.
- ✅ **Primary inputs are positional.** Solution name is `[CliArgument]`, component-id is `[CliArgument]`. Flags are for modifiers (`--type`, `--top`, `--show-json`).
- ✅ **`--yes` vs `--force-*` semantics.** Destructive commands (`component remove`, `layer remove-customization`) use `--yes` via `IDestructiveCommand`. No `--force` used.
- ✅ **No new short-flag aliases.** All flags are long-form. `-p` and `-f` remain the only short aliases.
- ✅ **Command aliases follow rules.** Group `component` gets alias `comp`. Leaf verbs (`list`, `get`, `add`, `remove`) stay spelled out. Aliases are for groups only.

### Architecture & Layering
- ✅ **Project layout matches rules.** CLI commands in `TALXIS.CLI.Features.Environment`, service contracts in `TALXIS.CLI.Core/Contracts/Dataverse/`, SDK implementations in `TALXIS.CLI.Platform.Dataverse.Application/Sdk/`.
- ✅ **Layering rules respected.** Features depend on Core only. Platform depends on Core. Features do NOT reference Platform directly. Provider selection happens at the host composition root via DI.
- ✅ **No Feature→Feature references.** Shared logic (DTOs, service contracts) goes into Core.
- ✅ **Two-layer service pattern.** Thin service classes in `Services/` delegate to SDK helper classes in `Sdk/` — matches existing `SolutionUninstaller`/`DataverseSolutionUninstallService` pattern.

### Output Contract
- ✅ **stdout/stderr separation.** All commands use `OutputFormatter` for stdout, `ILogger` via `TxcLoggerFactory` for stderr diagnostics.
- ✅ **`OutputFormatter` is the sole stdout API.** `WriteData`, `WriteList`, `WriteResult` used appropriately — no direct `Console.Write`.
- ✅ **`TXC003` pragma for text renderers.** All text-renderer callbacks that call `OutputWriter.WriteLine` are wrapped in `#pragma warning disable TXC003` / `#pragma warning restore TXC003`.
- ✅ **`--format json|text` inherited.** All leaf commands extend `TxcLeafCommand` (via `ProfiledCliCommand`), so `--format` is automatic.
- ✅ **Exit codes correct.** `ExitSuccess` (0) for success, `ExitValidationError` (2) for bad input, `ExitError` (1) for runtime failures.
- ✅ **No emojis or unicode.** All output uses plain ASCII — status labels are words like `"Safe to uninstall"`, `"Added"`, `"Removed"`.
- ✅ **No `--json` flags.** Uses inherited `--format` flag.

### Banned APIs
- ✅ **No `Console.Write*` or `Console.ReadKey`.** Uses `OutputWriter`/`OutputFormatter`.
- ✅ **No `Thread.Sleep`.** Async operations use `await`.
- ✅ **No `new HttpClient()`.** Uses `ServiceClient.ExecuteWebRequest()` for Web API calls.
- ✅ **No `throw new Exception()`.** Uses `InvalidOperationException`, `FaultException` handling, etc.
- ✅ **No Newtonsoft.Json in new code.** Uses `System.Text.Json` with `JsonDocument.Parse`. *(But see ⚠️ below for SolutionPackager dependency.)*
- ✅ **No `Task.Result` or `.GetAwaiter().GetResult()`.** All async paths use `await`.

### Build Conventions
- ✅ **Warnings-as-errors compliance.** Plan code follows patterns enforced by `RS0030`, `TXC001`, `TXC002`, `TXC003`.
- ✅ **`TXC001` compliance.** All leaf commands inherit `TxcLeafCommand` (via `ProfiledCliCommand`).
- ✅ **`TXC002` compliance.** No command defines its own `RunAsync()` — only `ExecuteAsync()`.
- ✅ **DTOs are `sealed record` types.** Immutable, value-equality, concise — matches the established pattern.

### Adding a Command (Checklist)
- ✅ **`[CliCommand]` with `TxcLeafCommand`/`ProfiledCliCommand`.** All planned commands follow this.
- ✅ **`protected override ILogger Logger`** implemented in every command.
- ✅ **`protected override Task<int> ExecuteAsync()`** implemented in every command.
- ✅ **Wired into parent's `Children` array.** Plan explicitly shows updates to `SolutionCliCommand.Children` and `EnvironmentCliCommand.Children`.
- ✅ **DI registration specified.** Each phase lists the `services.AddSingleton<>()` calls needed.

### SOLID & Interface Design
- ✅ **Interfaces are focused.** Plan splits `ISolutionManagementService` into `ISolutionDetailService`, `ISolutionCreateService`, `ISolutionPublishService` — SRP/ISP compliant.
- ✅ **Read/write separation.** `ISolutionComponentQueryService` (read) vs `ISolutionComponentMutationService` (write). `ISolutionLayerQueryService` (read) vs `ISolutionLayerMutationService` (write).
- ✅ **Incremental interface definition (I3).** `ISolutionDependencyService` starts with only `CheckUninstallAsync` in PR 1; additional methods added in PR 3. No `NotImplementedException` stubs.

---

## ⚠️ Rules the Plan Doesn't Address or May Partially Violate

### 1. `new JsonSerializerOptions()` — Banned API
**Rule:** `BannedSymbols.txt` bans `new JsonSerializerOptions()` — must use `TxcOutputJsonOptions.Default` or `TxcJsonOptions.Default`.
**Issue:** The plan uses `JsonDocument.Parse()` (which is fine — no `JsonSerializerOptions` needed for parsing). However, the `ComponentLayerGetCliCommand` pretty-prints JSON via a `PrettyPrintJson()` helper (01-inspection.md). If that helper creates `new JsonSerializerOptions { WriteIndented = true }`, it would violate the ban. The plan doesn't specify how `PrettyPrintJson` is implemented.
**Recommendation:** Ensure `PrettyPrintJson` reuses `TxcOutputJsonOptions.Default` (which already has `WriteIndented = true`).

### 2. SolutionPackager introduces Newtonsoft.Json as a transitive dependency
**Rule:** `BannedSymbols.txt` bans `Newtonsoft.Json.JsonConvert`.
**Issue:** `SolutionPackagerLib.dll` depends on `Newtonsoft.Json.dll` (listed in 00-infrastructure.md §5). While the *plan's own code* doesn't use Newtonsoft, the assembly reference brings it into the project. If any future code accidentally uses `JsonConvert`, the banned API analyzer would catch it — but the dependency itself may cause confusion or namespace collisions.
**Risk:** Low (analyzer will catch misuse), but worth documenting in the PR.

### 3. `[McpIgnore]` on destructive commands with `--yes`
**Rule:** `LayeringTests` enforces that `--yes` commands have `[McpIgnore]` (from `output-contract.md` enforcement table).
**Issue:** The plan shows `SolutionComponentRemoveCliCommand` and `ComponentLayerRemoveCustomizationCliCommand` implementing `IDestructiveCommand` with `--yes`, but does not mention adding `[McpIgnore]`. The existing `SolutionUninstallCliCommand` and `PackageUninstallCliCommand` set the pattern — check if they carry `[McpIgnore]` and replicate accordingly.
**Recommendation:** Explicitly annotate destructive commands with `[McpIgnore]` if the test suite requires it, or add them to `McpToolRegistry._longRunningCommandTypes` if they should be surfaced as async MCP tasks.

### 4. Long-running commands and MCP `_longRunningCommandTypes`
**Rule:** CONTRIBUTING.md §"Adding a command" item 5: *"If it is long-running and the MCP adapter should surface it as a task, add it to `McpToolRegistry._longRunningCommandTypes`."*
**Issue:** `sln publish` can take minutes on large environments (the plan notes this). `comp export` involves solution export + unpack. Neither is mentioned in the context of `_longRunningCommandTypes` registration.
**Recommendation:** Evaluate which new commands qualify as long-running and document the MCP registration decision.

### 5. Testing requirements not fully specified
**Rule:** `CommandConventionTests` and `LayeringTests` already exist and enforce conventions.
**Issue:** The plan mentions "how to verify" for infrastructure classes (unit tests for `ComponentTypeResolver`, etc.) but doesn't specify:
  - Whether new `CommandConventionTests` entries are needed for the new commands (they likely auto-discover via reflection, so probably no action needed).
  - Whether `LayeringTests` need updates for the new `Component/` project references.
  - Integration test strategy beyond "optional, manual."
**Recommendation:** Confirm existing convention tests auto-discover new commands. Plan at least one integration test per PR that can run against a test environment.

### 6. Invisible scaffolding pattern not used for future commands
**Rule:** CONTRIBUTING.md §"Invisible scaffolding": unreachable skeleton commands should be pinned in the codebase for reserved-but-not-yet-implemented commands.
**Issue:** Phase 4 (`sync`) commands are described in `04-sync.md` but no skeleton classes are proposed. If the command surface (e.g., `sln sync`, `sln diff`) is designed, skeletons could be created now.
**Recommendation:** Either create skeleton classes for Phase 4 commands or explicitly document that Phase 4 is too speculative for skeletons.

### 7. PR size and scope guidance
**Rule:** CONTRIBUTING.md §"Questions, disagreements, changes": *"change the document first, then write the code."* Also implicit: PRs should be reviewable.
**Issue:** The plan proposes reasonable PR splits (4 PRs for Phase 1, etc.), but some PRs are substantial. PR 1 (Phase 1) includes 4 commands + 3 service implementations + group scaffolding + DI wiring. This is on the larger side.
**Recommendation:** Consider whether PR 1 could be split into `sln get` + `sln component count/list` (sharing `ISolutionComponentQueryService`) and `sln uninstall-check` (adding `ISolutionDependencyService`) as a separate PR.

### 8. `SolutionCreateOutcome` missing from 02-crud.md DTO definition
**Rule:** Plan should be self-consistent — DTOs referenced in service interfaces should be defined.
**Issue:** `ISolutionCreateService.CreateAsync` returns `SolutionCreateOutcome`, but the DTO record definition for `SolutionCreateOutcome` appears only in the 02-crud.md command section, not in the Phase 0 contract definitions (00-infrastructure.md §4.1b). Phase 0 defines the interface but only shows `SolutionCreateOptions` in the code — `SolutionCreateOutcome` is referenced but not defined in that file.
**Recommendation:** Add `SolutionCreateOutcome` record to the Phase 0 contract definition in §4.1b.

### 9. `env component` alias `comp` collides with `env sln component` alias `comp`
**Rule:** CONTRIBUTING.md §"Command aliases": *"One alias per command."*
**Issue:** The plan defines both:
  - `txc env sln component` (alias `comp`) — components *within* a solution
  - `txc env component` (alias `comp`) — components *independent* of a solution
  Both groups use the alias `comp`. While they exist at different tree depths (so technically no collision in DotMake), it may cause user confusion (`txc env comp` resolves to `env component`, not `env sln comp`).
**Recommendation:** Verify DotMake disambiguation behavior. Consider dropping the alias from `sln component` or using a different alias to avoid UX confusion.

### 10. Documentation updates
**Rule:** CONTRIBUTING.md should be updated when the command surface changes (aliases table, reserved skeletons list, etc.).
**Issue:** The plan adds new aliases (`comp` for `env component`) and new group commands but doesn't mention updating the CONTRIBUTING.md aliases table or the README.md example snippets.
**Recommendation:** Add a documentation checklist item to each PR: update the aliases table in CONTRIBUTING.md and add example snippets to README.md.

---

## 🔴 Clear Violations

### 1. `ComponentTypeResolver` casts `IOrganizationServiceAsync2` to `ServiceClient` — violates abstraction boundary
**Rule:** CONTRIBUTING.md §"Introducing a platform": *"Feature projects depend on abstractions in TALXIS.CLI.Core, never directly on a Platform.\* project."* Architecture rule: *"Features depend on Core and Logging only."*
**Issue:** `ComponentTypeResolver.LoadScfTypesAsync()` accepts `IOrganizationServiceAsync2` but casts to `ServiceClient` internally. The plan acknowledges this as "SOLID fix D1" and calls it a pragmatic trade-off. However, `ServiceClient` lives in `Microsoft.PowerPlatform.Dataverse.Client` which is a platform SDK dependency. The class is in `Platform.Dataverse.Application` (correct placement), but the pattern of accepting an abstraction and then casting it down is a code smell that the plan itself flags.
**Severity:** Low — the class is correctly placed in Platform, not Features. The cast is guarded. This is more of a design smell than a layering violation. The plan addresses it transparently.

### 2. `SolutionComponentRemoveCliCommand` uses `[CliDestructive]` but plan doesn't show `[McpIgnore]`
**Rule:** `LayeringTests` enforces `--yes` commands missing `[McpIgnore]` are flagged as test failures.
**Issue:** The `SolutionComponentRemoveCliCommand` and `ComponentLayerRemoveCustomizationCliCommand` implement `IDestructiveCommand` (which includes `--yes`), but neither shows `[McpIgnore]` in the code sketch. If `LayeringTests` enforce this pairing, the build will fail.
**Severity:** Medium — this will be caught by tests, but the plan should specify the attribute to avoid a build-fix commit.
**Fix:** Add `[McpIgnore]` to both destructive command classes in 03-mutations.md.

### 3. `SolutionPublishCliCommand` verb "publish" is not in CONTRIBUTING.md's approved verb list
**Rule:** CONTRIBUTING.md §"Verbs": *"Prefer a small, consistent verb vocabulary: create, import, uninstall, list, show, validate, describe, patch."*
**Issue:** `publish` is not in the approved verb list. While `publish` is a well-understood Dataverse concept (PublishAllXmlRequest), the contributing rules explicitly enumerate the approved verbs and `publish` is absent.
**Severity:** Low — `publish` is a domain-specific term that doesn't collide with existing verbs, and the list says "prefer" not "only." However, per the rules: *"If you think the philosophy above is wrong for a specific case, open an issue or a draft PR that explains the case and proposes a targeted amendment to this document."*
**Fix:** Amend CONTRIBUTING.md to add `publish` to the verb vocabulary, then implement the command. Change the document first, then write the code.

### 4. `get` verb used instead of `show`
**Rule:** CONTRIBUTING.md §"Verbs": The approved verb for detail view is `show`, not `get`. §"`list` vs `show`": *"`show` — one run, full detail."*
**Issue:** The plan uses `sln get <name>` and `comp layer get <component-id>` instead of `sln show` and `comp layer show`. The CONTRIBUTING.md examples explicitly use `show` for single-item detail views: *"`environment deployment show --latest` — one run, full detail."*
**Severity:** Medium — this contradicts the established verb vocabulary. `get` is not in the approved list; `show` is the designated verb for single-item detail rendering.
**Fix:** Rename `sln get` → `sln show` and `comp layer get` → `comp layer show` to match the contributing rules. Update all references in the plan files.

---

## Summary

| Category | Count |
|----------|-------|
| ✅ Rules followed correctly | 25+ |
| ⚠️ Needs attention / partial gaps | 10 |
| 🔴 Clear violations | 4 |

**Critical fixes before implementation:**
1. Rename `get` → `show` to match the approved verb vocabulary (🔴 #4)
2. Add `publish` to CONTRIBUTING.md's verb list before implementing `sln publish` (🔴 #3)
3. Add `[McpIgnore]` to destructive commands (🔴 #2)
4. Verify `env component` vs `sln component` alias collision (⚠️ #9)

The plan is overall **well-aligned** with the contributing rules. The architecture, layering, output contract, and banned API compliance are thorough. The main gaps are in verb naming conventions and a few MCP integration details.
