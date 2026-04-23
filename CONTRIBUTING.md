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
- **Platforms live internally.** Platform-specific code lives under `Platforms/<Name>/` inside the owning project (e.g. `TALXIS.CLI.Features.Environment/Platforms/Dataverse/`). Do **not** create a `<Name>CliCommand` — platforms are not command groups.
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

Short-flag aliases (`-v`, `-y`, etc.) are out of scope until there is a concrete usability case for a specific flag. Long-form flags only until then.

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
- **Short-alias exception for `--profile`.** Because `config profile select` is the single most frequently typed command on a dev laptop, the `--profile` flag on every auth-requiring leaf command exposes the short form `-p`. This is the only flag-level short alias in the CLI.

---

## Output conventions

- **`OutputWriter` for command result data on stdout.** Anything a script might parse or a user might pipe.
- **`ILogger` for diagnostics, progress, warnings, and errors.** Goes to stderr. Respects `TXC_LOG_FORMAT` and `TXC_LOG_LEVEL`.
- **Plain ASCII only.** No emojis, no unicode icons, no box-drawing characters. Status labels are words like `OK`, `FAILED`, `STUCK`.
- **`TXC_LOG_FORMAT=json`** (or stdout redirected to a non-TTY) switches logging to structured JSON on stderr.

---

## Adding a command

1. Create a class with `[CliCommand]` in the appropriate project and folder.
2. Wire it into its parent's `Children = new[] { typeof(...) }` array.
3. If it is long-running and the MCP adapter should surface it as a task, add it to `McpToolRegistry._longRunningCommandTypes`.

DotMake and the MCP adapter discover the rest from the tree. You do not register the command in two places.

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

1. Add `Platforms/<Name>/` inside the owning project (e.g. `TALXIS.CLI.Features.Environment/Platforms/Azure/`).
2. Put all platform-specific services in that folder, in a `TALXIS.CLI.Features.Environment.Platforms.<Name>` namespace.
3. Keep the command classes platform-agnostic: a single `Package.PackageImportCliCommand` dispatches to the right platform internally.
4. At this point (and not before), extract a shared abstraction — e.g. `IEnvironmentPlatform` — from the two real shapes. Do not write the interface before the second implementation exists.

Do not add a `<Name>CliCommand`. Platforms are implementation details; they do not surface as command groups.

---

## Questions, disagreements, changes

If you think the philosophy above is wrong for a specific case, open an issue or a draft PR that explains the case and proposes a targeted amendment to this document. The rule is: change the document first, then write the code that follows it.
