# Connectors, Connections and Connection References

## Key Concepts

Three different things, each with its own commands. Mixing them up is the most common mistake.

| Concept | What it is | Commands |
|---|---|---|
| **Connector** | The *type* of integration: Teams, SharePoint, or a custom connector you built. Defines the authentication methods and operations. | `environment_connector_list`, `environment_connector_get` |
| **Connection** | A stored *sign-in* for one connector in one environment, owned by an identity (a person or a service principal). Never exported with a solution; its id differs on every environment. | `environment_connection_create`, `environment_connection_list`, `environment_connection_get`, `environment_connection_test`, `environment_connection_delete`, `environment_connection_permission_add`, `environment_connection_permission_list`, `environment_connection_permission_remove` |
| **Connection reference** | A solution component with a **stable logical name** that points at a connection of a connector. Flows and apps bind to the reference, so a solution carries no connection ids and no credentials. | `environment_connection-reference_list`, `environment_connection-reference_get`, `environment_connection-reference_create`, `environment_connection-reference_update`, `environment_connection-reference_delete` |

This is not the `config_connection_create` family, which stores *local* environment endpoints for profiles.

## Naming a Connector

Pass the connector's **logical name** to `--connector` (optional on commands that act on an existing connection id; needed only when the same id exists under several connectors). For a custom connector it is stable across environments (for example `almlab_connectorsopenfoodfacts`); for a built-in connector it is the apiId (`shared_office365users`). `environment_connector_list` and `environment_connector_get` show both as `logicalName` and `apiId`; the apiId which for a custom connector carries an environment-specific hash that you should not hard-code. An apiId or an exact display name also works.

## Creating a Connection

`environment_connection_create` works for every connector and is idempotent by connector and display name: an existing connected connection is returned as `unchanged`.

| Connector needs | How |
|---|---|
| Nothing (a no-auth custom connector) | `--connector` and `--display-name` only. |
| A user to sign in (OAuth) | Same. With a user credential (browser or device code) it signs in silently when the connector supports it. Otherwise it returns `status: consent-required` with a `consentLink` and exits 1; a person must open the link. `--no-consent` skips the silent attempt. |
| A service principal | `--auth-method` (see `environment_connector_get`), `--param name=value` for non-secrets and `--secret-from-env name=ENV_VAR` for secrets. The platform validates the credentials immediately; wrong ones fail with the platform's message and leave nothing behind. |

Secrets are never accepted on the command line. A freshly created client secret can take up to a minute to propagate; a connection created too early fails with `AADSTS7000215` and succeeds on retry.

A service-principal profile cannot sign in to a user-delegated OAuth connector: consent needs a user. Either use a service-principal authentication method, or have a person create the connection and **share** it (below).

## Results and Exit Codes

Every mutating command returns a `status` (`created`, `updated`, `unchanged`, `assigned`, `removed`, ...) and, when something needs attention, a `statusMessage`; `environment_connection_test` returns `healthy` or `unhealthy`. Exit code `1` means consent-required, failed or unhealthy and **still carries the payload** (read it before retrying); `2` means invalid input or not found (unknown connector, connection or reference, ambiguous name, missing environment variable). `connection-reference` create and update return the reference including its `id`.

## Deployment Recipe (connection references)

1. Import the solution that contains a **custom connector** first. Connection references to it cannot be created before the connector exists, and canvas apps do not recognise references to custom connectors.
2. Make sure the connection exists: `environment_connection_create`.
3. If a different identity (usually the pipeline's service principal) will bind the reference or turn the flow on, **share** the connection with it: `environment_connection_permission_add` with `--service-principal <client id>`.
4. Import the solution, then bind each reference: `environment_connection-reference_update` with `--connection-id`. Binding is idempotent.
5. Verify nothing is left unbound: `environment_connection-reference_list` with `--unbound` returns an empty list.

Create a reference by hand with `environment_connection-reference_create` (use `--solution` to put it in a solution and `--connection-id` to bind it in one step).

## Sharing a Connection

`environment_connection_permission_add` grants a principal a role; `environment_connection_permission_list` shows who has access; `environment_connection_permission_remove` revokes it.

- Principals: `--user` (UPN or object id), `--service-principal` (client id or object id), `--group` (object id). Exactly one.
- Roles (kebab-case only): `can-view` (can use the connection; the default and the least needed for a pipeline), `can-view-with-share`, `can-edit`. The owner always keeps access and cannot be removed.
- Without sharing, binding a reference or enabling a flow as another identity fails with `ConnectionAuthorizationFailed`.
- A connection owned by another identity does not appear in `environment_connection_list` until it is shared with you.

Sharing a user-delegated connection lets the grantee act with that user's delegated access. Grant the least role and review `environment_connection_permission_list`.

## Gotchas

- **Connection ids are opaque.** Use them exactly as listed; do not reformat them.
- **A reference binding cannot be cleared** (the platform ignores it). Delete and recreate the reference instead.
- **`bindingStatus`** on a reference: `bound`, `unbound`, `broken` (the connection exists but is not connected: fix or replace it), or `unknown` (the connection was deleted or is not visible to you).
- **Copying an environment breaks references to custom connectors**; create new references there.
- Destructive commands (`*_delete`, `_permission_remove`) need `--yes`, and `--allow-production` when the environment type is unknown or production.
