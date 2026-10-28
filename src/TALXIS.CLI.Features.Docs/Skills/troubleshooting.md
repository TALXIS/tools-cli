# Troubleshooting Common Issues

> **Running these commands.** Commands are written for the CLI: `txc <command>` (`txc <command> --help` lists its arguments and options). With the TALXIS MCP server connected, most commands are also tools, named by joining the command words with `_` (`txc workspace component create` is `workspace_component_create`; hyphens stay, as in `environment_solution_uninstall-check`). If a tool is not in your tool list, run it through the server's `execute_operation` tool. A tool's input schema names its arguments, and those names can differ from the CLI options. A link such as `[plugin-development](plugin-development.md)` is a doc id: `txc docs get plugin-development` prints it.

## Deployment Failures

**Start here:** `txc environment deployment get --latest` (or `txc environment deployment get --async-operation-id <guid>`, using the id that `txc environment solution import` printed)

> Never query the `asyncoperation` table directly via `txc environment data query sql` — it returns raw status codes and unparsed error XML. `txc environment deployment get` gives you structured findings with human-readable error messages.

Commands that act on one component take `--id <guid> --type <type>` or `--entity <logicalName> [--attribute <name>]` (the type is detected when `--entity` is used).

```
Import failed
  ├─→ Error: Component conflict
  │    └─→ txc environment component layer list --id <component-guid> --type <type>
  │         └─→ Identify which solution owns the conflicting component
  │              └─→ Update that solution first, or remove the conflict
  │
  ├─→ Error: Missing dependency
  │    └─→ txc environment component dependency required --id <component-guid> --type <type>
  │         └─→ Import the solution containing the required component first
  │
  ├─→ Error: Solution already exists (version conflict)
  │    └─→ Increment the solution version and retry
  │
  └─→ Error: Timeout or generic failure
       └─→ Check environment health, then retry the import (`--wait` only when the caller has no timeout; otherwise poll `deployment get`, see deployment-workflow)
```

## Component Conflicts

When a component behaves unexpectedly or modifications don't take effect:

1. **Inspect layers**: `txc environment component layer list --entity <logicalName>` (or `--id <guid> --type <type>`) — see all solutions that customize this component
2. **Examine specific layer**: `txc environment component layer get --entity <logicalName>` (same selectors) — understand what each solution contributes
3. **Resolution**: The topmost layer wins. Either:
   - Update the topmost managed solution
   - Remove the unmanaged active layer
   - Reorder solution imports

## Authentication Issues

```
Commands fail with 401/403 or connection errors
  └─→ txc config profile validate [<profile>]
       ├─→ "Invalid" → txc config profile get [<profile>] to check the connection and auth names
       │    └─→ txc config connection get <connection> to verify the URL; txc config auth get <alias> for the credential
       │         └─→ Re-create auth: txc config auth add-service-principal (see environment-management)
       │
       └─→ "Valid" but still failing
            └─→ Check if the service principal has required security roles
                 └─→ Verify environment-level access in Power Platform admin center
```

## Dependency Problems

### Can't Delete a Component
```
txc environment component dependency delete-check --entity <logicalName> [--attribute <name>]
```
(or `--id <guid> --type <type>`.) Shows what depends on the component you're trying to delete. Remove or update those dependencies first.

### Missing Required Dependencies
```
txc environment component dependency required --entity <logicalName> [--attribute <name>]
```
(or `--id <guid> --type <type>`.) Shows what components are required by a given component. Ensure all dependencies are present in the target environment.

## Wrong Environment

If data or schema doesn't match expectations:
1. `txc config profile get` — shows the active profile and its connection; `txc config connection get <connection>` shows the environment URL
2. Confirm you're connected to dev/test/prod as intended
3. Switch profiles if needed: `txc config profile select <name>`, or add `--profile <name>` to a single command

## Quick Reference

| Symptom | First Command to Run |
|---|---|
| Import failed | `txc environment deployment get --latest` |
| Component conflict | `txc environment component layer list --entity <logicalName>` |
| Can't delete something | `txc environment component dependency delete-check --entity <logicalName>` |
| Missing dependency | `txc environment component dependency required --entity <logicalName>` |
| Auth errors | `txc config profile validate` |
| Wrong data showing | `txc config profile get` |
| Changes not visible | `txc environment solution publish` |

## Common Dataverse Error Codes

| Hex Code | Meaning | Recovery |
|---|---|---|
| `0x80040216` | Entity relationship already exists | Skip or use force-overwrite on import |
| `0x80048d19` | Duplicate alternate key value | Check key columns for unique constraint violations |
| `0x80040237` | Record does not exist | Verify the GUID; record may have been deleted |
| `0x8004431a` | Privilege denied | Check security role assignments for the user |
| `0x80060891` | Async operation failed | Check async operation status for details |
| `0x80040220` | Attribute already exists | Skip or import with overwrite flag |

### Metadata Lock Contention
If schema operations fail with "another operation is running against this entity," the entity metadata is locked by a concurrent operation. Wait 10-30 seconds and retry. Avoid parallel schema changes on the same entity.

See also: [deployment-workflow](deployment-workflow.md), [solution-layering](solution-layering.md)
