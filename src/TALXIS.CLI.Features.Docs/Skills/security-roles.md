# Security Roles

## Scaffolding Chain

1. **Create the role** — `pp-security-role` template inside an existing solution project.
2. **Add privileges** — `pp-security-role-privilege` template to grant table-level access.
3. **Assign to app** — `pp-app-security-role` template to bind the role to a model-driven app.

## Scope Resolution for `txc security ... --environment`

For `txc security user`, `txc security service-principal`, `txc security role`, and `txc security team`, the `--environment <id>` flag always means the same thing: run the Dataverse environment-scoped implementation for that environment instead of the tenant-wide implementation.

Resolution order:

1. If `--environment <id>` is passed, `txc` uses that Dataverse environment.
2. Otherwise, if the resolved profile is already connected to an environment, `txc` uses that active environment.
3. Otherwise, `txc` falls back to the tenant-wide Entra / Power Platform admin implementation when that command supports one.

`txc security team ...` has no tenant-wide fallback. It always requires `--environment` or an active environment connection.

## Catalog vs Assignment Lists

- `txc security role list [--environment <id>]` switches catalogs. Without an environment it lists tenant admin roles; with an environment it lists that environment's Dataverse security-role catalog. The catalogs are never combined.
- `txc security user role list ... [--environment <id>]` and `txc security service-principal role list ... [--environment <id>]` list assignments. Without an environment they show tenant admin roles only. With an environment they show **both** tenant admin roles and that environment's Dataverse security-role assignments under separate labeled sections.

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

## Assigning Roles to Users, Service Principals, and Teams

1. **Find the role**
   - Tenant catalog: `txc security role list [--filter <name>]`, `txc security role get --role <name-or-guid>`
   - Dataverse catalog: `txc security role list --environment <id> [--filter <name>]`, `txc security role get --environment <id> --role <name-or-guid>`
2. **Find the principal**
   - User (tenant): `txc security user list [--filter <upn-or-name>]`, `txc security user get --user <upn-or-object-id>`
   - User (Dataverse): `txc security user list --environment <id> [--enabled|--disabled|--all]`, `txc security user get --environment <id> --user <upn-or-guid>`
   - Provision a Dataverse user immediately: `txc security user add --environment <id> --user <upn-or-object-id> [--role <name-or-guid>[,<name-or-guid>,...]]`
   - Bootstrap yourself into a Dataverse environment: `txc security user self-elevate --environment <id>`
   - Service principal (tenant): `txc security service-principal list [--filter <name>]`, `txc security service-principal get --service-principal <client-id-or-object-id>`
   - Service principal (Dataverse): `txc security service-principal list --environment <id> [--enabled|--disabled|--all]`, `txc security service-principal get --environment <id> --service-principal <client-id-or-guid>`
   - Create a Dataverse service principal: `txc security service-principal create --environment <id> --service-principal <entra-client-id> [--business-unit <name-or-guid>] [--role <name-or-guid>[,<name-or-guid>,...]]`
   - Team (Dataverse only): `txc security team list --environment <id>`, `txc security team get --environment <id> --team <name-or-guid>`, `txc security team create --environment <id> --name <name> --type owner|access|aad-security-group|aad-office-group [--aad-object-id <guid>] [--membership-type <..>] [--business-unit <name-or-guid>]`
3. **Assign or revoke the role**
   - Tenant user roles: `txc security user role add --user <upn-or-object-id> --role <name-or-guid>` / `txc security user role remove --user .. --role ..`
   - Dataverse user roles: `txc security user role add --environment <id> --user <upn-or-guid> --role <name-or-guid>` / `txc security user role remove --environment <id> --user .. --role ..`
   - Tenant service-principal roles: `txc security service-principal role add --service-principal <client-id-or-object-id> --role <name-or-guid>` / `txc security service-principal role remove --service-principal .. --role ..`
   - Dataverse service-principal roles: `txc security service-principal role add --environment <id> --service-principal <client-id-or-guid> --role <name-or-guid>` / `txc security service-principal role remove --environment <id> --service-principal .. --role ..`
   - Dataverse team roles: `txc security team role add --environment <id> --team <name-or-guid> --role <name-or-guid>` / `txc security team role remove --environment <id> --team .. --role ..`

## Tenant-Wide Admin Roles (`txc security ...` without `--environment`)

These commands manage admin-level access across the whole tenant. They never create or modify anything in Entra ID — they only discover principals that already exist there and manage their tenant-wide admin roles.

- `txc security service-principal role add/remove ...`
- `txc security user role add/remove ...`
- `txc security group role add/remove ...`

For applications only, `--role admin-application` is a special value: it authorizes that app to call `txc` environment admin commands (`create`/`list`/`update`/`delete`) non-interactively. Unlike every other role, this one is implemented through the Power Platform Admin (BAP) API's `adminApplications` endpoint, so `role list` marks it with `"isSynthetic": true`.
