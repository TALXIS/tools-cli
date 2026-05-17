# Session Analysis: PCT21016 Merge Dialog Bug Fix

## Session Overview

| Metric | Value |
|--------|-------|
| Duration | ~3 hours |
| Tokens | ↑81.4M input (79.8M cached) • ↓154.9K output |
| Estimated cost | ~€53 (Azure Foundry, Opus 4.6) |
| Repos touched | INT0006 (source fix), PCT21016 (workspace), INT0015-Portal (investigation), INT0014 (investigation) |
| Outcome | Draft PR #23783 with 3 fixes, live-tested on devbox-2788 |

## Session Timeline & Friction Points

### Phase 1: Bug Investigation (~40 min)

**What happened:** User provided screenshots of `context.getEntityName is not a function` error from CASE-8346. Had to search 4 repos to find the TypeScript source for `talxis_asyncjobs.js`.

**Friction points:**
- **Cross-repo code discovery is manual.** The webresource was referenced in PCT21016 XML declarations but the TS source lived in INT0006. Agent had to grep 4 repos to find it.
- **No dependency graph between repos.** Nothing tells the agent that PCT21016 consumes webresources built from INT0006.
- **Webresource → source file mapping doesn't exist.** `talxis_asyncjobs.js` is a build artifact; finding the TS source required searching for namespace patterns.

**What would help (Copilot CLI / agent-level):**
- Cross-repo workspace awareness — agent could register multiple local repos and search them in parallel
- Repository relationship metadata — a config that declares dependencies between repos
- Webresource name → source project resolution using `.csproj` ScriptLibraryName / tsconfig outFile

### Phase 2: Root Cause Analysis (~20 min)

**What happened:** Traced the call chain: `MergeRecords` → `triggerAsyncJobAction` → `Main(dialogContext)` → `GetAsyncJobs` → crash. Identified 3 bugs.

**Friction points:**
- **No TypeScript LSP available.** Call chain tracing was done via grep + manual file reading. No go-to-definition or find-references.
- **Compiled JS → TS source correlation.** Error screenshot showed `talxis_asyncjobs.js:337` but agent had to manually map to the TS source line.

**What would help (Copilot CLI / agent-level):**
- TypeScript LSP integration for navigation in old-style projects
- Sourcemap resolution tool (compiled JS line → TS source location)

### Phase 3: Live Reproduction with Playwright (~50 min)

**What happened:** Opened Power Apps and Portal in headed browsers. Navigated to contacts, performed merge, monitored console. Used `window.__unhandledErrors` listener to prove the race condition.

**Friction points:**
- **No built-in console error monitoring.** Had to manually inject `unhandledrejection` listener, then query `window.__unhandledErrors` after each action.
- **Checkbox clicking fails in Dynamics grids.** Playwright couldn't click `<input type="checkbox">` in UCI grids (timeout). Had to click parent gridcell instead.
- **Dynamics 365 navigation is many steps.** App picker → app → sitemap → entity → select rows → ribbon button = 6+ Playwright commands with waits.
- **Playwright snapshots of Dynamics pages are token-expensive.** Each YAML snapshot is 5-10K tokens.

**What would help (Playwright CLI):**
- `--watch-errors` flag — auto-inject error listeners, surface new errors after each command
- `--retry-click` — when click times out on an element, retry with parent element
- Smarter snapshots — option to capture only a section of the page (e.g. `--selector "[role=dialog]"`)

### Phase 4: Live Patching & Testing (~30 min)

**What happened:** Monkey-patched JS functions in the browser to test fixes before committing. Went through 4 iterations because of serialization issues and incorrect patch scope.

**Friction points:**
- **`page.evaluate` fails with complex JS.** Serialization issues with `&&`, arrow functions. Had to write patches to files and use `addScriptTag`.
- **Patches don't survive page reload.** Each reload required re-injection.
- **No way to verify patch is active.** Had to manually check `fn.toString()` to confirm the right function was patched.

**What would help (Playwright CLI):**
- `inject-script <file>` command with auto-re-injection on reload
- Better `page.evaluate` serialization for complex expressions

### Phase 5: Building & Deploying (~25 min)

**What happened:** Compiled TypeScript manually (`npx tsc`) because old SDK doesn't build on macOS. Imported solutions via TXC CLI.

**Friction points:**
- **Old `TALXIS.SDK.BuildTargets.CDS` requires PowerShell.** `dotnet build` fails on macOS. Had to manually run `npx tsc` and copy output.
- **Solution import failed first time** — target had managed solution, import attempted unmanaged. Had to retry with `--managed`.
- **Build artifacts accidentally committed** multiple times. `git add -A` picked up `.js` files copied into `Declarations/WebResources/`.
- **No profile for target environment.** Had to manually create connection + profile for devbox-2788 before import.

**What would help (TXC CLI / MCP):**
- **Auto-detect managed/unmanaged mismatch** — when `environment_solution_import` fails with "managed solution already installed as unmanaged", auto-retry with `--managed` (or vice versa), or at minimum suggest it clearly in the error output.
- **Profile bootstrap from environment URL** — when user mentions an environment URL in conversation, the agent could check if a profile exists and suggest creating one. The `config_profile_create --url` flow should work in headless mode when an existing interactive credential matches the tenant.

### Phase 6: PR Creation & Conventions (~20 min)

**What happened:** Created branch, committed, pushed, created draft PR on Azure DevOps. Multiple iterations to match team conventions.

**Friction points:**
- **PR conventions had to be reverse-engineered.** Queried 15 completed PRs to discover title format (`Area/Module - description`), branch naming (`users/{name}/{desc}`), and description template (checklist).
- **Wrong branch name initially.** `fix/merge-dialog-errors` instead of `users/tomas.prokop/...`. Had to rename, delete remote, re-push, abandon old PR, create new.
- **PR description went stale** after code changes. Had to manually update twice.
- **Work item discovery required WIQL query** to find #52082 from CASE-8346.

**What would help (Copilot CLI / agent-level):**
- PR convention auto-detection from repo history (title patterns, branch naming, templates)
- Work item search from external ticket references (Jira CASE-xxxx → Azure DevOps work item)

### Phase 7: Code Review Iterations (~15 min)

**What happened:** User reviewed diff and caught issues: Portal-specific error handling, over-broad context check skipping grid callers, missing code comments.

**Friction points:**
- **Agent didn't trace all call paths.** Used `DetermineIfContextIsEntityForm` which would also skip grid contexts — user caught this.
- **Didn't question existing patterns.** Existing `if (window.TALXIS?.Portal)` check in nearby code wasn't flagged as inconsistent.

**What would help (Copilot CLI / agent-level):**
- Before committing, trace all callers of modified functions and verify no regressions
- Flag inconsistent patterns between new and existing error handling in the same file

## TXC-Specific Improvements

Only two friction points directly involved TXC:

| # | Issue | Improvement | Effort |
|---|-------|-------------|--------|
| 1 | Solution import managed/unmanaged mismatch gives unhelpful error | Auto-detect and suggest or retry with correct mode | Low |
| 2 | No profile for target env, `--url` bootstrap fails headless | Allow `--url` to reuse existing interactive credential matching the tenant without launching a browser | Medium |

Everything else (cross-repo search, Playwright issues, PR conventions, build tooling) is outside TXC scope.

## Token Cost Observations

- **98% of input tokens were cache hits** — Anthropic prompt caching working effectively
- **Output was only 154.9K** — agent reads far more than it writes
- **Sub-agents (5 explore agents) add to token count** — each gets full context
- **Playwright YAML snapshots are expensive** — Dynamics 365 pages generate 5-10K token snapshots

### Token Reduction Opportunities (Copilot CLI level)

1. **Smarter Playwright snapshots** — capture only relevant DOM section, not full page
2. **Sub-agent result summarization** — compress explore agent findings before injecting into main context
3. **Conversation compaction** — after completing a phase, summarize history to key findings
4. **File read deduplication** — several files were read multiple times across turns

## Azure Foundry Billing Context

Monthly bill for the Foundry Opus 4.6 deployment: **€3,491**

| Meter | Cost | % |
|-------|------|---|
| Cache hits (long ctx) | €1,430 | 41% |
| Cache writes (long ctx) | €1,051 | 30% |
| Cache hits (standard) | €403 | 12% |
| Cache writes (standard) | €369 | 11% |
| Output | €226 | 6% |
| Plain input | €13 | <1% |

- 94% cache hit rate across all usage
- 73% of cost is long-context pricing
- This single session ≈ 1.5% of monthly bill
