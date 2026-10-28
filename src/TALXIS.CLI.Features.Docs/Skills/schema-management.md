# Schema CRUD on Live Environments

## When to Use Environment Schema Commands

> **For source-controlled development, prefer local scaffolding** via `txc workspace component create <type> --output <project-dir>` (`--param <name>=<value>` sets template parameters). The environment schema commands are for:
> - **Inspection** — understanding what's deployed in an environment
> - **Troubleshooting** — diagnosing schema issues in live environments
> - **Quick fixes** — emergency changes in non-production environments
> - **Prototyping** — rapid experimentation before codifying locally
>
> When the user explicitly asks for a change on the live environment, use these commands: the user's request wins over this default.

## Available Operations

Run `txc environment entity --help` (and `--help` on any subcommand) for the exact arguments. Operations cover entities, attributes, relationships, and option sets:

| Area | Commands |
|---|---|
| Tables | `txc environment entity list\|describe\|get\|create\|update\|delete` |
| Columns | `txc environment entity attribute get\|create\|update\|delete`; column types: `txc environment entity attribute type list` and `type describe <type>` |
| Relationships | `txc environment entity relationship create\|list\|delete` |
| Option sets | `txc environment optionset list\|get\|create\|delete`; single options: `txc environment optionset option add\|remove` |

Every create, update and delete command above needs exactly one of `--apply` (run now) or `--stage` (add to a changeset, run later with `txc environment changeset apply --strategy batch|transaction|bulk`). Deletes also ask for confirmation unless `--yes` is passed. Examples:

```
txc environment entity create --name <schemaName> --display-name "<Label>" --plural-name "<Labels>" --solution <solution-name> --apply
txc environment entity attribute create <entityLogicalName> --name <schemaName> --type <type> --solution <solution-name> --apply
txc environment entity describe <entityLogicalName>
```

## Key Rules

- Run `txc environment component dependency delete-check --entity <logicalName> [--attribute <name>]` before deleting a component
- The create, update and delete commands for tables, columns and many-to-many relationships publish the affected table themselves. After other changes (for example option sets) run `txc environment solution publish` (`--entities <names>` limits it) if the change does not show up
- Environment schema changes are **not tracked in source control** — codify locally afterward
- Changes in managed layers can't be undone — only overridden by a new managed import

## Decision Tree

- **Routine development** → `txc workspace component create` (local, source-controlled)
- **Inspecting what's deployed** → `txc environment entity list|describe|get`
- **Emergency non-production fix** → the environment commands, then codify locally
- **Comparing deployed vs local** → `txc environment entity describe <entityLogicalName>` shows the entity's attributes in the live env

## What NOT to Do

- ❌ Don't use these commands for routine development — changes bypass source control
- ❌ Don't delete tables/columns without running `txc environment component dependency delete-check` first
- ❌ Don't assume a change is live when it does not show up: run `txc environment solution publish`

## Metadata Propagation

When creating multiple entities with relationships via environment commands, follow a phased approach:

1. Create **all tables** first (no lookups)
2. Wait 15-30 seconds for metadata to propagate
3. Create **all lookup columns** and relationships
4. Wait again before creating forms/views that reference the lookups

**Column Naming Warning:** Never suffix custom column names with `Id` (e.g., `new_accountId`). Dataverse auto-generates navigation properties with that suffix for lookups, causing collisions and 400 errors.

**Lock Contention:** If you get "another operation is running against this entity" errors, wait and retry. Multiple concurrent schema changes on the same entity cause lock conflicts.

See also: [component-creation](component-creation.md), [solution-layering](solution-layering.md), [troubleshooting](troubleshooting.md)
