# Command Taxonomy: `security` vs `governance` vs `environment`

This document explains where identity/access and tenant-governance commands
live in `txc`, and why — so the split is discoverable without trial and
error.

## The three top-level groups

```
txc environment   — one connected org: ALM, schema, data, solution, component, entity. No RBAC.
txc security      — all RBAC (identity/access), tenant + Dataverse, unified via a --environment scope flag.
txc governance    — tenant-wide governance configuration with no single-environment connection: environment groups, rule-based policies.
```

### `txc environment` — connected-org data & lifecycle only

`environment` commands all require "connected to one specific org" as
their organizing reason: ALM (`solution`), schema (`entity`, `component`),
and data (`data`). It has **no RBAC commands** — identity/access questions
("who can do what") are a different kind of question from "what does this
org's schema/data/solution look like," so they live in `security` instead,
even for a connected Dataverse environment.

### `txc security` — every RBAC question, one flag

`security` answers "who has access, and to what" — for the tenant
(Entra/Power-Platform-admin identities and roles) and for a specific
Dataverse environment (systemusers, teams, security roles), through one
uniform scope flag:

```
txc security user               list/get/create/update/delete/role   [--environment <id>]
txc security service-principal  list/get/create/update/delete/role   [--environment <id>]
txc security role               list/get                            [--environment <id>]
txc security team               list/get/create/update/delete/member/role   --environment <id> (required)
txc security group              list/get/role                       (Entra security groups; tenant-only)
```

**`--environment <id>` means exactly one thing everywhere it appears**:
"scope this RBAC operation to this Dataverse environment instead of the
tenant-wide directory." Rules:

- It **defaults to the active profile's environment connection** when one
  is set, so a connected user doesn't need to repeat an id they've already
  supplied.
- It **can be passed explicitly** to target any environment's RBAC without
  switching the active connection.
- `security team` has **no tenant-wide equivalent** (Dataverse teams have
  no Entra analog), so `--environment` is **required** there, not optional
  — never a silently-changes-behavior toggle.
- `security group` (Entra security groups) has **no Dataverse equivalent**,
  so it takes no flag at all — nothing to disambiguate.

#### Why a flag, when a flag-based design was rejected for `pac`

`pac admin`'s flag usage is inconsistent per-verb (`list-groups`,
`add-group`, `assign-user`, `create-service-principal`, each with its own
one-off flag conventions), so a flag never reliably means the same thing
twice. `txc security`'s flag is **structural, not verb-by-verb bolted on**:
every RBAC resource that has both a tenant and a Dataverse form exposes the
*same* `--environment` flag, with the *same* fallback/override behavior,
documented once here instead of once per command.

#### Catalog listing switches scope; assignment listing combines scope

- `security role list [--environment <id>]` lists a **role catalog** — a
  definition set. Tenant admin roles and Dataverse security roles are
  non-overlapping catalogs, so `--environment` **switches** which catalog
  you see; there is no meaningful way to merge two different catalogs.
- `security user role list` / `security service-principal role list`
  `[--environment <id>]` list a principal's **actual assigned roles** —
  their real, effective access. A principal's tenant admin role (if any)
  applies everywhere; their Dataverse security roles apply only within
  environment(s) they're a member of. So when `--environment <id>` is
  supplied, this command **combines** both under separate labeled
  sections in one call, instead of requiring two invocations to piece
  together someone's full access picture.

See [Skills/security-roles.md](../src/TALXIS.CLI.Features.Docs/Skills/security-roles.md)
for the full worked sequence (find role → find principal → assign/revoke).

### `txc governance` — tenant-wide rules, no single-environment connection

`governance` covers configuration that spans or organizes *multiple*
environments and has no single-connection framing:

```
txc governance environment-group   list/get/create/update/delete, environment add/remove, role list/add/remove
txc governance policy-rule         list/get/create/update/remove-rule, assign, assignment list
```

- **`environment-group`** is a tenant-level folder of managed
  environments and the attachment point for both governance rules and
  RBAC role assignments held directly on the group (not on any one member
  environment).
- **`policy-rule`** is the modern rule-based-policy framework replacing
  classic DLP policies. It targets the confirmed "Advanced Connector
  Policy" rule type today (`--allow-connector` shorthand); other rule
  types can be authored via `--rule-set-inputs-json` once their shapes are
  confirmed by Microsoft.

Both are genuinely new capabilities with no existing `environment`/
`security` leaf to collide with, so no scope flag or naming exception is
needed for them.

#### Confirmed API gaps, deliberately not worked around

As of this writing, the Power Platform governance REST API does not
expose a delete-policy or an unassign/remove-assignment operation. `txc`
does **not** fake these with unsupported workarounds — `policy-rule`'s own
command descriptions and `IPowerPlatformPolicyRuleClient`'s XML docs both
call this out explicitly, along with the closest supported alternative
(excluding one environment from a group-wide assignment via
`--exclude-environment` on `assign`). Extend the interface once Microsoft
adds these operations; do not build a synthetic delete/unassign around
missing API support.

## Why RBAC is the one exception to "organize by connection scope"

Everywhere else in `txc`, the top-level split is: does this command need a
live connection to one specific org (`environment`), or is it tenant-wide
configuration (`governance`)? RBAC deliberately doesn't follow that split.
"Who has access, and to what" is one coherent question regardless of which
backing system (Dataverse or Entra/Power-Platform-admin) answers it — a
genuinely different shape of question from "what does this org's
schema/data/solution look like" (`environment`) or "which local files am I
editing" (`workspace`). Consolidating all RBAC under `security`, with scope
expressed as a flag rather than as a top-level location, matches that
mental model and removes a real discoverability problem: `environment` and
`security` no longer share a single leaf name (`user`, `service-principal`,
`team`, `role`) for conceptually different things.
