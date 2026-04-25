# Contributing to the TALXIS CLI

This document captures the design philosophy of the TALXIS CLI (`txc`) command surface. It exists so contributors adding new commands stay on-rails with decisions that have already been made, rather than re-litigating them per PR.

If you want to add a command, change a command's shape, or introduce a new surface area, please read this first.

---

## Top-level taxonomy

The CLI has five top-level groups, in this order, by design:

```
txc
├── config        # identity the CLI acts as (profiles, connections, credentials, settings)
├── workspace     # the local code repository (scaffold, build, validate, language-server, metamodel)
├── environment   # the live target runtime footprint the workspace deploys to
├── data          # data migration, transformation, imports
└── docs          # knowledge base for TALXIS CLI
```

These five groups are deliberately small. Adding a sixth top-level group requires a strong justification — if a new piece of functionality fits under an existing noun, put it there.

### `config` sub-nouns

The `config` group has four sub-nouns, each owning one aspect of the resolution pipeline:

```
txc config
├── auth         # credentials (OAuth tokens, service principals) stored in the OS vault
├── connection   # service endpoint metadata (Dataverse environments, etc.)
├── profile      # named binding of one auth × one connection — the "context" users switch between
└── setting      # tool-wide preferences (log.level, log.format, telemetry.enabled)
```

The separation is deliberate. Connections are *where*, credentials are *who*, profiles are the *context* mapping them, and settings are tool-level knobs unrelated to any identity. Do not collapse them or teach one sub-noun to write into another's store.

---

## Nouns, not platforms, in command paths

Command paths describe **what the user is doing**, not **what platform implements it**.

- **No platform names in user-facing paths.** The word `dataverse` does not appear in any command path, and neither will any future platform name (`azure`, `entra`, `graph`, etc.). Users should not need to know or care which runtime their workspace artifacts land on.
- **Platforms live in dedicated projects.** Platform-specific implementations live in `TALXIS.CLI.Platform.<Name>` projects (e.g. `TALXIS.CLI.Platform.Dataverse`). Feature projects depend on abstractions in `TALXIS.CLI.Core`, never directly on a `Platform.*` project. Provider registration happens in the host (`TALXIS.CLI` / `TALXIS.CLI.MCP`). Do **not** create a `<Name>CliCommand` — platforms are not command groups.
- **Abstractions are extracted, not speculated.** When a second platform is actually implemented, extract an interface from the shape that already exists. Do not speculate an `IEnvironmentPlatform` (or similar) before there is a second concrete implementation to validate it.

We avoid even the term "backend" for this abstraction: a Dataverse environment carries metadata for frontend (forms, apps), middle tier (business logic, plugins, workflows), and integrations. Calling it a backend understates what ships there.

---

## Verbs

Prefer a small, consistent verb vocabulary:

- `create` — scaffold a new artifact in the workspace.
- `import` — push an artifact into a live environment.
- `uninstall` — remove an artifact from a live environment.
- `list` — enumerate artifacts of a noun, with lightweight filtering.
- `show` — render the details of a single artifact.
- `validate` — check the workspace against the metamodel.
- `describe` — render a human-readable description of a metamodel entity.
- `patch` — reserved for incremental push workflows.

When an artifact is installed into a live environment, `import` and `uninstall` come as a pair — if you add one, you usually owe the other.

---

## `list` vs `show`

Standard list-vs-detail pair. Both are always scoped by the owning noun:

- `environment solution list` — many solutions, brief rows.
- `environment deployment list` — many deployment runs, brief rows.
- `environment deployment show --latest` — one run, full detail.

If a command wants to render many things *and* full detail, it is doing two jobs — split it.

---

## Typed selectors, not generic `--id`

When a `show`-style lookup can resolve against multiple entity shapes, expose **one flag per entity shape** and let the user declare intent. Do **not** accept a generic `--id` that the command probes across entity types behind the user's back.

Example — `environment deployment show`:

- `--package-run-id <GUID>` — packagehistory record id.
- `--solution-run-id <GUID>` — msdyn_solutionhistory record id.
- `--async-operation-id <GUID>` — asyncoperation id returned by a queued solution import.
- `--package-name <name>` — latest run by NuGet package name.
- `--solution-name <name>` — latest run by solution unique name.
- `--latest` — most recent across both.

Exactly one selector must be provided; the command routes directly to the right reader. No cross-entity probing, no "did you mean" heuristics, no silent fallbacks.

Why: the old generic `--id` required the user to know what kind of id they had **and** forced the CLI to call two or three Dataverse endpoints per invocation until one responded. The typed-selector form makes intent explicit, error messages actionable, and behavior deterministic.

---

## Primary inputs are positional

The primary input to a command — the thing it is operating on — is positional. Do not smuggle it into a flag.

- `txc environment package import <package-source>` — not `--source`.
- `txc environment solution import <solution-zip>` — not `--source`.

Flags are for modifiers (`--environment`, `--connection-string`, `--yes`, `--stage-and-upgrade`, etc.), not for the subject of the command.

---

## `--yes` vs `--force-*`

These are not interchangeable and are not aliased.

- `--yes` — "I have read the confirmation, skip the prompt." Used on destructive actions (e.g. `uninstall`).
- `--force-<something>` — "override this one specific safety rail." Names the rail it overrides (e.g. `--force-overwrite`).

A `--force` flag by itself is ambiguous and is not used.

---

## Short-flag forms

Short-flag aliases are reserved for high-frequency flags only. Currently allocated:

- `-p` for `--profile` (on `ProfiledCliCommand` — the most frequently typed option)
- `-f` for `--format` (on `TxcLeafCommand` — inherited by every leaf command)

All other flags remain long-form only. Do not add new short aliases without updating this list.

---

## Command aliases

Long, explicit names are the canonical form, but **group** commands (the ones that hold children, not leaves) carry an `Alias` so day-to-day typing and README snippets stay short. Current aliases:

| Canonical             | Alias   |
| --------------------- | ------- |
| `config`              | `c`     |
| `config profile`      | `p`     |
| `environment`         | `env`   |
| `env deployment`      | `deploy`|
| `env package`         | `pkg`   |
| `env solution`        | `sln`   |
| `data package`        | `pkg`   |
| `workspace`           | `ws`    |
| `workspace component` | `c`     |
| `workspace project`   | `p`     |

Rules:

- **Aliases are for groups, not leaf verbs.** `import`, `list`, `show`, `uninstall`, `create`, `convert` etc. stay spelled out — they're already short and a single letter would be ambiguous.
- **Canonical names drive everything machine-readable.** MCP tool names, help anchors, the SDK surface — all built from `Name`. Aliases are a typing shortcut for humans; they never leak into tool IDs.
- **One alias per command.** If you find yourself reaching for a second alias, rename the canonical instead.
- **Prefer README and docs to use the alias** in example snippets — that's what the alias is for. Use the canonical name in reference tables, help output, and anywhere a reader needs to scan the full taxonomy.
- **Short-alias exception for `--profile` and `--format`.** The `--profile` flag exposes `-p` on every `ProfiledCliCommand` leaf. The `--format` flag exposes `-f` on every `TxcLeafCommand` leaf. These are the only flag-level short aliases in the CLI.

---

## Output contract

**See [`docs/output-contract.md`](docs/output-contract.md) for the full specification.** Summary:

- **stdout = result data, stderr = diagnostics.** Never mix them.
- **`OutputFormatter`** is the only API commands use for stdout output — not `OutputWriter` directly, not `Console.Write`.
- **`ILogger`** (via `TxcLoggerFactory`) for diagnostics, progress, warnings, and errors. Always goes to stderr.
- **`--format json|text`** inherited by every leaf command from `TxcLeafCommand`. Defaults to text in terminals, JSON when piped.
- **Exit codes:** `0` = success, `1` = runtime error, `2` = input/validation error.
- **Plain ASCII only.** No emojis, no unicode icons, no box-drawing characters. Status labels are words like `OK`, `FAILED`, `STUCK`.
- **`TXC_LOG_FORMAT=json`** (or stdout redirected to a non-TTY) switches logging to structured JSON on stderr.
- **`BannedApiAnalyzers`** enforces `Console.Write*` and `Console.ReadKey` are never used in command code (build error).
- **`CommandConventionTests`** enforces all leaf commands inherit `TxcLeafCommand`, implement `ExecuteAsync()`, and have no stale `--json` flags.

---

## Safety annotations

Every leaf command **must** declare exactly one of three safety attributes. The `TXC004` analyzer enforces this as a build error.

| Attribute | Meaning | MCP behaviour |
|-----------|---------|---------------|
| `[CliReadOnly]` | No side effects — pure read | Auto-approved; no human confirmation needed |
| `[CliIdempotent]` | Safe to retry; produces the same result on re-execution | MCP clients may auto-retry on transient failures |
| `[CliDestructive("impact message")]` | Irreversible or dangerous operation | Always requires confirmation |

**Rules:**

- `[CliDestructive]` requires a non-empty `impact` string describing consequences. The message is shown in interactive prompts and in MCP `ToolAnnotations.Title`.
- Commands annotated with `[CliDestructive]` **must also implement `IDestructiveCommand`**, which exposes the `--yes` flag. Without `--yes`, the CLI prompts interactively; in headless/CI environments the command fails with `ExitValidationError`.
- `[CliIdempotent]` can be combined with `[CliDestructive]` (e.g. an overwrite that is safe to retry but still destructive).
- `[CliReadOnly]` and `[CliDestructive]` are mutually exclusive.

## `--apply` / `--stage` execution mode

Mutating commands that extend `StagedCliCommand` expose two flags: `--apply` (execute immediately) and `--stage` (queue in the local changeset). Exactly one must be provided.

This pattern decouples _intent_ (what the user wants to change) from _execution_ (when/how the change reaches the server), enabling the changeset staging workflow described in [docs/changeset-staging.md](docs/changeset-staging.md).

---

## Adding a command

1. Create a class with `[CliCommand]` extending `TxcLeafCommand` (or `ProfiledCliCommand` for environment-facing commands).
2. Implement `protected override ILogger Logger { get; }` and `protected override Task<int> ExecuteAsync()`.
3. Use `OutputFormatter` for all stdout output. Use `ExitSuccess`/`ExitError`/`ExitValidationError` return values.
4. Wire it into its parent's `Children = new[] { typeof(...) }` array.
5. If it is long-running and the MCP adapter should surface it as a task, add it to `McpToolRegistry._longRunningCommandTypes`.

DotMake and the MCP adapter discover the rest from the tree. You do not register the command in two places. See [`docs/output-contract.md`](docs/output-contract.md) for the full output specification.

---

## Invisible scaffolding

Classes decorated with `[CliCommand]` that are **not** referenced from any parent's `Children` array are unreachable from DotMake's command tree. They therefore:

- do not appear in `txc --help`,
- are not invocable from the CLI,
- are not surfaced as MCP tools.

We use this mechanism to pin the design of reserved-but-not-yet-implemented commands in the codebase. A contributor reading the tree can see where the surface is heading, but cannot accidentally use a half-built command.

Current reserved skeletons:

- `TALXIS.CLI.Features.Environment.Deployment.DeploymentPatchCliCommand` → future `environment deployment patch`.
- `TALXIS.CLI.Features.Workspace.WorkspaceValidateCliCommand` → future `workspace validate`.
- `TALXIS.CLI.Features.Workspace.WorkspaceLanguageServerCliCommand` → future `workspace language-server`.
- `TALXIS.CLI.Features.Workspace.Metamodel.MetamodelCliCommand` + `MetamodelDescribeCliCommand` + `MetamodelListCliCommand` → future `workspace metamodel {describe,list}`.

Each skeleton throws `NotImplementedException` and carries a file-top comment explaining that it is intentionally unreachable and how to activate it.

**Activating a skeleton is a two-edit change:**

1. Add `typeof(TheSkeletonCliCommand)` to the relevant parent's `Children` array.
2. Replace the `NotImplementedException` body with the real implementation.

Do **not**:
- Add a `Parent = typeof(...)` back-pointer on the skeleton. That defeats unreachability — DotMake registers the class through the back-pointer regardless of `Children`.
- Rename a skeleton into a different noun because "it's not used yet." The name is part of the pinned design; if the name needs to change, change it with intent and update this document.

---

## Naming: user data model vs. framework metamodel

The workspace contains two different "models" and we name them differently on purpose:

- **data model** — what the user authors (Dataverse tables, columns, relationships, etc.). Owned by the user's workspace content. Referred to as "data model" in docs and prose.
- **metamodel** — the grammar that describes what a valid workspace artifact looks like. Owned by the CLI. Drives validation, the language server, and richer scaffolding. Exposed under `workspace metamodel`.

Never call the metamodel "the model" — it collides with the user's data model and confuses readers.

---

## Introducing a platform

When a second runtime platform actually needs to be supported:

1. Add a new `TALXIS.CLI.Platform.<Name>` project (e.g. `TALXIS.CLI.Platform.Azure`) alongside the existing `Platform.*` projects.
2. Put all platform-specific services in that project, under a `TALXIS.CLI.Platform.<Name>` namespace.
3. Register the provider's services in an `AddTxc<Name>Provider` extension, and wire it up from the host composition roots (`TALXIS.CLI/Program.cs` and `TALXIS.CLI.MCP/Program.cs`).
4. Keep the command classes platform-agnostic: a single `Package.PackageImportCliCommand` resolves the right implementation via the DI container, it does not reference a `Platform.*` project directly.
5. At this point (and not before), extract a shared abstraction in `TALXIS.CLI.Core` — e.g. `IEnvironmentPlatform` — from the two real shapes. Do not write the interface before the second implementation exists.

Do not add a `<Name>CliCommand`. Platforms are implementation details; they do not surface as command groups.

---

## Project layout

Projects are grouped by architectural role. Names must reflect this role:

- **Hosts** — thin entrypoints that compose DI and register commands. Nothing else.
  - `TALXIS.CLI` — the `txc` CLI host.
  - `TALXIS.CLI.MCP` — the MCP server host.
- **Features** — user-facing command surfaces and orchestration, organised by domain.
  - `TALXIS.CLI.Features.Config`, `TALXIS.CLI.Features.Data`, `TALXIS.CLI.Features.Environment`, `TALXIS.CLI.Features.Workspace`, `TALXIS.CLI.Features.Docs`.
- **Core** — contracts, models, configuration, vault, resolution, shared utilities.
  - `TALXIS.CLI.Core`.
- **Platform** — external-system adapters and SDK integration.
  - `TALXIS.CLI.Platform.Dataverse`, `TALXIS.CLI.Platform.Xrm`, `TALXIS.CLI.Platform.XrmShim`.
- **Cross-cutting** — infrastructure.
  - `TALXIS.CLI.Logging`.

**Layering rules:**

- Features depend on `Core` and `Logging` only. Features do **not** reference `Platform.*` projects.
- Platform depends on `Core` (and external SDKs). Platform does **not** reference `Features.*`.
- Hosts reference everything they need for composition: `Features.*`, `Platform.*`, `Core`, `Logging`.
- No feature references another feature. Shared logic goes into `Core`.
- Provider selection happens at the host composition root, never inside a command handler.

---

## Questions, disagreements, changes

If you think the philosophy above is wrong for a specific case, open an issue or a draft PR that explains the case and proposes a targeted amendment to this document. The rule is: change the document first, then write the code that follows it.
