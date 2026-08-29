# Governance: Environment Groups & Rule-Based Policies

## Scope

`txc governance` manages tenant-wide governance configuration that spans
or organizes multiple environments — as opposed to `txc security`, which
manages identity/access (RBAC) for the tenant or for one connected
Dataverse environment. See `docs/command-taxonomy.md` in the repository
root for the full rationale.

Classic DLP policies are out of scope — Microsoft is replacing them with
the rule-based-policy framework this doc covers, so `txc` targets only the
modern framework.

## End-to-End Workflow

The typical governance sequence, in order:

1. **Create an environment group** — a tenant-level folder that organizes
   managed environments and serves as the attachment point for both role
   assignments and policy rules.
   ```sh
   txc governance environment-group create --display-name "Finance environments" [--description "..."]
   ```
2. **Add member environments to the group.** Only managed environments can
   belong to a group; each environment can belong to at most one group at
   a time. The environment immediately inherits every rule already
   published on the group.
   ```sh
   txc governance environment-group environment add <environment-group> --environment <id>
   ```
3. **Grant access to the group** via RBAC role assignments held directly on
   it — these apply to every current and future member environment.
   ```sh
   txc governance environment-group role add <environment-group> --principal-type User --principal <upn-or-object-id> --role Contributor
   txc governance environment-group role list <environment-group>
   ```
4. **Create a rule-based policy.** The only confirmed rule type today is
   the Advanced Connector Policy (`ConnectorManagement`), an allow-list of
   connectors — connectors not listed are blocked by default.
   ```sh
   # Allow every action on Office 365, and only two actions on SQL:
   txc governance policy-rule create --name "Finance connector policy" \
     --allow-connector shared_office365 \
     --allow-connector "shared_sql=ExecuteProcedure,GetRows"
   ```
5. **Assign the policy** to the environment group (or a single
   environment). Assigning to a group applies it to every current and
   future member; use `--exclude-environment` (repeatable) to exempt
   specific members from a group-wide assignment.
   ```sh
   txc governance policy-rule assign <policy> --environment-group <environment-group-id>
   txc governance policy-rule assign <policy> --environment-group <environment-group-id> --exclude-environment <env-id>
   txc governance policy-rule assign <policy> --environment <environment-id>
   txc governance policy-rule assignment list [--policy <id> | --environment-group <id> | --environment <id>]
   ```

## Adding or Updating Rule Sets on an Existing Policy

`update` is additive — existing rule sets not targeted by the call are
left untouched:

```sh
# Rename the policy and/or add/replace one rule set:
txc governance policy-rule update <policy> --name "New name" --allow-connector shared_office365

# Remove one rule set entirely (the policy itself is not deleted):
txc governance policy-rule remove-rule <policy> --rule-set-id ConnectorManagement
```

For rule types other than `ConnectorManagement`, author the `inputs` JSON
directly once its shape is confirmed, instead of `--allow-connector`:

```sh
txc governance policy-rule create --name "..." --rule-set-id <type> --rule-set-inputs-json '{...}'
```

## Confirmed API Gaps (Not Worked Around)

As of this writing, the Power Platform governance REST API does not
expose:

- **Deleting a policy.** Nothing to work around — leave unused policies
  unassigned instead.
- **Unassigning a policy from a resource.** To stop enforcing a group-wide
  policy on one member environment, reassign the policy to the group with
  that environment added to `--exclude-environment` instead of unassigning
  it outright.

`policy-rule`'s own command descriptions and `IPowerPlatformPolicyRuleClient`
document this explicitly. `txc` does not fake these operations with
unsupported workarounds — extend the client once Microsoft adds them.

## Deleting an Environment Group

Deletion is rejected (`409 Conflict`) while the group still has member
environments or assigned policy rules. Remove members
(`environment-group environment remove`) first; there is currently no
`--force` cascading delete (tracked as a follow-up).
