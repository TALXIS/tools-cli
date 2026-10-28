# Schema CRUD on Live Environments

## When to Use Environment Schema Commands

> **For source-controlled development, prefer local scaffolding**: `txc workspace component create <type> --output <project-dir>` (`--param <name>=<value>` sets template parameters). Use the environment commands for inspection, troubleshooting, non-production emergency fixes and prototyping. When the user explicitly asks for a change on the live environment, use them: the user's request wins.

## Available Operations

`txc environment entity --help` (and `--help` on any subcommand) gives exact arguments.

| Area | Commands |
|---|---|
| Tables | `txc environment entity list\|describe\|get\|create\|update\|delete` |
| Columns | `txc environment entity attribute get\|create\|update\|delete`; types: `txc environment entity attribute type list`, `type describe <type>` |
| Relationships | `txc environment entity relationship create\|list\|delete` |
| Option sets | `txc environment optionset list\|get\|create\|delete`; options: `txc environment optionset option add\|remove` |

Every create, update and delete needs exactly one of `--apply` (run now) or `--stage` (add to a changeset, run later with `txc environment changeset apply --strategy batch|transaction|bulk`). Deletes also ask for confirmation unless `--yes`.

```
txc environment entity create --name <schemaName> --display-name "<Label>" --plural-name "<Labels>" --solution <solution-name> --apply
txc environment entity attribute create <entityLogicalName> --name <schemaName> --type <type> --solution <solution-name> --apply
txc environment entity describe <entityLogicalName>
```

## Key Rules

- Run `txc environment component dependency delete-check --entity <logicalName> [--attribute <name>]` before deleting a table or column
- Create, update and delete for tables, columns and many-to-many relationships publish the affected table themselves. After other changes (e.g. option sets), if the change doesn't show up, run `txc environment solution publish` (`--entities <names>` limits it)
- Environment schema changes are not tracked in source control: codify locally afterward
- Changes in managed layers can't be undone, only overridden by a new managed import
- Compare deployed vs local: `txc environment entity describe` shows the live attributes

## Creating Several Entities

1. Create all tables first (no lookups)
2. Wait 15-30 seconds for metadata to propagate
3. Create all lookup columns and relationships
4. Wait again before creating forms/views that reference the lookups

Never suffix custom column names with `Id` (e.g. `new_accountId`): lookups auto-generate navigation properties with that suffix, causing collisions and 400 errors. On "another operation is running against this entity", wait and retry; don't run concurrent schema changes on one entity.

See also: [component-creation](component-creation.md), [solution-layering](solution-layering.md), [troubleshooting](troubleshooting.md)
