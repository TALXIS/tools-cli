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
