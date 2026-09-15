# Security Roles

## Scaffolding Chain

1. **Create the role** — `pp-security-role` template inside an existing solution project.
2. **Add privileges** — `pp-security-role-privilege` template to grant table-level access.
3. **Assign to app** — `pp-app-security-role` template to bind the role to a model-driven app.

## Privilege Types

| Privilege | Purpose |
|---|---|
| Create | Create new records |
| Read | View records |
| Write | Update existing records |
| Delete | Remove records |
| Append | Attach a record to another (child side) |
| AppendTo | Allow other records to attach to this record (parent side) |
| Assign | Change record ownership |
| Share | Share a record with another user/team |

## Privilege Levels

| Level | Scope |
|---|---|
| None | No access |
| User / Basic | Own records only |
| BusinessUnit | Records in the user's business unit |
| ParentChild | Records in the user's BU and child BUs |
| Global | All records in the organization |

## Design Pattern: One Role Per Persona

Create a dedicated role for each user persona (e.g., `Sales Rep`, `Warehouse Manager`). Avoid catch-all roles — they make auditing and least-privilege enforcement difficult.

## PrivilegeTypeAndLevel Format

When using `pp-security-role-privilege`, specify privileges as a JSON array:
```json
[
  { "type": "Read", "level": "Global" },
  { "type": "Write", "level": "BusinessUnit" },
  { "type": "Create", "level": "User" },
  { "type": "Delete", "level": "None" }
]
```
Each entry maps a privilege type to the desired depth. Omitted types default to `None`.

## Assigning Roles to Environment Users, Service Principals, and Teams

Once a security role exists (scaffolded above, or already present in the target environment),
use these commands to find it and assign it to whoever needs it. All three principal kinds
share the same shape: `list`/`get` to find the principal, `role list/add/remove` to manage its
roles.

1. **Find the role** — `txc environment role list [--filter <name>]` /
   `txc environment role get --role <name-or-guid>`. This is a read-only browse of the roles
   already defined in the target environment (via the scaffolding chain above or Dataverse's
   built-in roles) — accepts either the role name or its GUID everywhere `--role` is used below.
2. **Find the principal:**
   - Regular (human) user: `txc environment user list [--enabled|--disabled|--all]` /
     `txc environment user get --user <upn-or-guid>`. Regular users are normally provisioned by
     background Entra sync the first time they sign in, so `list`/`get`/`update` (enable/disable)
     is usually all you need. If the user has never signed in to this environment yet and you
     need to assign a role before that happens, use `txc environment user add --user
     <upn-or-object-id> [--role <name-or-guid>[,<name-or-guid>,...]]` to provision access
     immediately — safe to run again for a user who already has access. To grant *yourself*
     admin access when you have none at all in this environment, use `txc environment user
     self-elevate` instead (a different, tenant-admin-privileged bootstrap path — see its own
     help text).
   - Service principal: `txc environment service-principal list [--enabled|--disabled|--all]`
     / `txc environment service-principal get --service-principal <client-id-or-guid>`. If the service principal doesn't
     exist yet, create it directly (the Entra app registration itself must already exist):
     `txc environment service-principal create --service-principal <entra-client-id> [--business-unit <name-or-guid>]
     [--role <name-or-guid>[,<name-or-guid>,...]]` — `--role` accepts a comma-separated list to
     assign initial roles in the same step.
   - Team: `txc environment team list` / `txc environment team get --team <name-or-guid>`. If it
     doesn't exist yet: `txc environment team create --name <name> --type
     owner|access|aad-security-group|aad-office-group [--aad-object-id <guid>]
     [--membership-type <..>] [--business-unit <name-or-guid>]`, then (for `owner`/`access` teams
     only — AAD-backed team membership is managed in Entra ID) `txc environment team member add
     --team <name-or-guid> --user <upn-or-guid>`.
3. **Assign or revoke the role:**
   - `txc environment user role add --user <upn-or-guid> --role <name-or-guid>` /
     `txc environment user role remove --user .. --role ..`
   - `txc environment service-principal role add --service-principal <client-id-or-guid> --role <name-or-guid>` /
     `txc environment service-principal role remove --service-principal .. --role ..`
   - `txc environment team role add --team <name-or-guid> --role <name-or-guid>` /
     `txc environment team role remove --team .. --role ..`
   - Use `role list --user|--service-principal|--team ..` at any point to see currently assigned roles.

## Tenant-Wide Admin Roles (`txc tenant ...`)

The commands above manage access **within one environment**. A separate, tenant-wide set of
commands manages admin-level access **across the whole tenant** — e.g. who can administer
Power Platform environments, connectors, or DLP policies tenant-wide. These commands never
create or modify anything in Entra ID — they only discover principals that already exist there
and manage their tenant-wide admin roles.

1. **Find the tenant role** — `txc tenant role list [--filter <name>]` /
   `txc tenant role get --role <name-or-guid>`. This is the catalog every `--role` value below
   is validated against.
2. **Find the principal:**
   - `txc tenant service-principal list [--filter <name>]` / `txc tenant service-principal get --service-principal <client-id-or-object-id>`
   - `txc tenant user list [--filter <upn-or-name>]` / `txc tenant user get --user <upn-or-object-id>`
   - Groups have no `list`/`get` — see the note below.
3. **Assign or revoke the tenant role:**
   - `txc tenant service-principal role add --service-principal .. --role <name-or-guid>` / `role remove --service-principal .. --role ..`
   - `txc tenant user role add --user .. --role <name-or-guid>` / `role remove --user .. --role ..`
   - `txc tenant group role add --group <object-id> --role <name-or-guid>` /
     `role remove --group <object-id> --role ..`. Unlike service principals and users, `--group` must be the
     group's Entra **object id (GUID)** — this CLI never looks groups up by display name, because
     that would require the Microsoft Graph `Group.Read.All` permission, which is not
     pre-consented for this CLI's Entra app registration in most tenants. This CLI never prompts
     for extra consent, so find the object id yourself first, e.g. via the Entra admin center or
     `az ad group show --group <name> --query id -o tsv`.
   - For applications only, `--role admin-application` is a special value: it authorizes that
     app to call this CLI's own `environment` admin commands (`create`/`list`/`update`/`delete`)
     non-interactively, modeled as just another role the app can hold — not a separate
     create/delete concept. Unlike every other role, this one is **not** a Power Platform RBAC
     role assignment: it is implemented by registering the application with the Power Platform
     Admin (BAP) API's `adminApplications` endpoint, the same mechanism Microsoft documents at
     [Create a service principal to create and manage environments and other resources for
     Power Platform](https://learn.microsoft.com/en-us/power-platform/admin/powerplatform-api-create-service-principal).
     Because it uses a different API, `role list` marks it with `"isSynthetic": true` in its
     output (every real RBAC role assignment shows `"isSynthetic": false`) — this is how a
     consumer scripting against the JSON output can tell the two apart. Attempting
     `--role admin-application` against a `--user` or `--group` fails with a validation error,
     since only applications can be registered as environment-management service principals.
   - Use `role list --app|--user|--group ..` at any point to see currently assigned tenant roles.

