# Connectors, connections and connection references

`txc env connector`, `txc env connection` and `txc env connection-reference` manage the three things a Power Platform integration is made of. They work against the live environment of the active profile (see [profiles-and-authentication.md](profiles-and-authentication.md)) and are not related to `txc config connection`, which stores *local* environment endpoints.

A **connector** is the type of integration, a **connection** is a stored sign-in for one connector (per environment, never exported, its id differs everywhere), and a **connection reference** is a solution component with a stable logical name that points at a connection. Flows bind to connection references so a solution never carries connection ids or credentials.

## Connectors

```sh
txc env connector list [--filter <text>] [--kind built-in|custom] [--top <n>]
txc env connector get <connector> [--filter <text>]
```

Commands that act on an existing connection take its id; `--connector` is then optional and only needed when the same id exists under several connectors.

Use the **logical name** shown by `connector list` wherever a command takes `--connector`. For a custom connector it is the same in every environment, unlike its apiId, which carries an environment-specific hash. An apiId or an exact display name also works.

## Connections

```sh
txc env connection create --connector <c> --display-name <name> \
    [--auth-method <name>] [--param name=value ...] \
    [--secret-from-env name=ENV_VAR ...] [--no-consent]
txc env connection list [--connector <c>] [--filter <text>]
txc env connection get <connection-id> [--connector <c>]
txc env connection test <connection-id> [--connector <c>]
txc env connection delete <connection-id> [--connector <c>] --yes
```

`create` is idempotent by connector and display name and works without a browser:

```sh
# A custom connector that needs no authentication
txc env connection create --connector almlab_connectorsopenfoodfacts --display-name "Food Facts"

# A user-delegated OAuth connector, signed in silently with a user credential
txc env connection create --connector shared_office365users --display-name "Mail"

# A service principal; the secret is read from the environment, never from the command line
txc env connection create --connector shared_commondataserviceforapps --display-name "Dataverse (deploy)" \
    --auth-method ServicePrincipalOauth \
    --param token:clientId=<app id> --param token:TenantId=<tenant id> \
    --secret-from-env token:clientSecret=DEPLOY_SECRET
```

Connectors that need a person to sign in (no on-behalf-of support) return `status: consent-required` and a `consentLink`, and exit 1. A service-principal profile cannot complete user consent. `connection list` shows the connections you own or can use; one created by another identity appears once it is shared with you.

The `status` of `create` is `created`, `updated`, `unchanged`, `consent-required` or `failed`; `connectionStatus` is the platform's own state.

## Sharing a connection

```sh
txc env connection permission list <connection-id> [--connector <c>]
txc env connection permission add <connection-id> [--connector <c>] \
    (--user <upn|id> | --service-principal <client id|object id> | --group <id>) [--role can-view|can-view-with-share|can-edit]
txc env connection permission remove <connection-id> [--connector <c>] (same principal options) --yes
```

Binding a connection reference, or turning on a flow, is checked as the acting identity: it must own the connection or have it shared (`ConnectionAuthorizationFailed` otherwise). A pipeline that runs as a service principal therefore needs the connection shared with it. `can-view` ("can use") is enough. The owner cannot be removed.

## Connection references

```sh
txc env connection-reference list [--solution <name>] [--connector <c>] [--connection-id <id>] [--unbound] [--filter <text>]
txc env connection-reference get <logical-name>
txc env connection-reference create <logical-name> --connector <c> --display-name <name> [--solution <name>] [--connection-id <id>]
txc env connection-reference update <logical-name> [--connection-id <id>] [--display-name <name>]
txc env connection-reference delete <logical-name> --yes
```

`bindingStatus` is `bound`, `unbound`, `broken` (the connection exists but is not connected) or `unknown` (deleted, or not visible to you). A binding cannot be cleared; delete and recreate the reference.

### Deploying with references

The step-by-step recipe (connector import order, sharing with the pipeline identity, binding, verifying nothing is unbound) lives in the agent skill [`connection-management.md`](../src/TALXIS.CLI.Features.Docs/Skills/connection-management.md), which is the single authoritative copy.

## Output and exit codes

All commands support `--format json|text`. Every mutating command reports a `status` (`created`, `updated`, `unchanged`, `assigned`, `removed`; `connection test` reports `healthy` or `unhealthy`), a `statusMessage` when there is something to explain, and the same fields as the matching `get`; connection-reference `create` and `update` include the reference `id`. `connector get` lists operations under `logicalName` and `apiId`.

| Exit | Meaning |
|---|---|
| 0 | Done (including `unchanged`). |
| 1 | Consent required, failed or unhealthy; the payload is still written. |
| 2 | Invalid input or not found: unknown connector, connection or reference, ambiguous name, bad `--param`, missing environment variable. |
